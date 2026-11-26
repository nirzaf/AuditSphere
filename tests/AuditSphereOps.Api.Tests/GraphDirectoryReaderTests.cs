using System.Net;
using System.Text;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Infrastructure.Providers;

namespace AuditSphereOps.Api.Tests;

public sealed class GraphDirectoryReaderTests
{
  private static readonly string Tenant = Guid.NewGuid().ToString("D");
  private static readonly string Client = Guid.NewGuid().ToString("D");
  private static readonly DirectoryCertificateOptions Options = new(true, Tenant, Client, "/private/cert.pem", "/private/key.pem");

  [Fact]
  public async Task SearchIsBoundedToExpectedTenantAndFields()
  {
    var objectId = Guid.NewGuid().ToString("D");
    Uri? requested = null;
    var handler = new StubHandler(request =>
    {
      requested = request.RequestUri;
      Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
      return Json($$"""{"value":[{"id":"{{objectId}}","displayName":"Ada","userPrincipalName":"ada@example.test","accountEnabled":true,"userType":"Member"}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/users?$skiptoken=next-page"}""");
    });
    var reader = new GraphDirectoryReader(new HttpClient(handler), new StubTokenSource(
      new("opaque", Tenant, new HashSet<string> { "User.Read.All" })), Options);
    var page = await reader.SearchAsync(Tenant, "Ad", null, default);
    Assert.Single(page.Users);
    Assert.Equal(objectId, page.Users[0].ObjectId);
    Assert.True(page.Users[0].AccountEnabled);
    Assert.Equal("next-page", page.NextPageToken);
    Assert.Equal("graph.microsoft.com", requested?.Host);
    Assert.Contains("$top=25", requested?.Query, StringComparison.Ordinal);
    Assert.Contains("$select=id,displayName,userPrincipalName,accountEnabled,userType", requested?.Query, StringComparison.Ordinal);
  }

  [Fact]
  public async Task ActiveBrowseUsesBoundedAdvancedDomainFilter()
  {
    var objectId = Guid.NewGuid();
    var handler = new StubHandler(request =>
    {
      Assert.Equal("eventual", request.Headers.GetValues("ConsistencyLevel").Single());
      var query = Uri.UnescapeDataString(request.RequestUri!.Query);
      Assert.Contains("$count=true", query, StringComparison.Ordinal);
      Assert.Contains("$top=25", query, StringComparison.Ordinal);
      Assert.Contains("accountEnabled eq true and endsWith(userPrincipalName,'@example.test')", query,
        StringComparison.Ordinal);
      return Json($$"""{"value":[{"id":"{{objectId}}","displayName":"Ada","userPrincipalName":"ada@example.test","accountEnabled":true,"userType":"Member"}]}""");
    });
    var reader = new GraphDirectoryReader(new HttpClient(handler), new StubTokenSource(
      new("opaque", Tenant, new HashSet<string> { "User.Read.All" })), Options);
    Assert.Single((await reader.BrowseActiveAsync(Tenant, "example.test", null, default)).Users);
  }

  [Fact]
  public async Task PrefixSearchUsesServerSideDomainFilterAndRejectsCrossDomainResponse()
  {
    var objectId = Guid.NewGuid();
    var handler = new StubHandler(request =>
    {
      Assert.Equal("eventual", request.Headers.GetValues("ConsistencyLevel").Single());
      var query = Uri.UnescapeDataString(request.RequestUri!.Query);
      Assert.Contains("$count=true", query, StringComparison.Ordinal);
      Assert.Contains("startsWith(displayName,'Green') and endsWith(userPrincipalName,'@example.test')",
        query, StringComparison.Ordinal);
      return Json($$"""{"value":[{"id":"{{objectId}}","displayName":"Green","userPrincipalName":"green@other.test","accountEnabled":true,"userType":"Member"}]}""");
    });
    var reader = new GraphDirectoryReader(new HttpClient(handler), new StubTokenSource(
      new("opaque", Tenant, new HashSet<string> { "User.Read.All" })), Options);
    await Assert.ThrowsAsync<OperationBlockedException>(() =>
      reader.SearchAsync(Tenant, "Green", null, default, "example.test"));
  }

  [Fact]
  public async Task ActiveBrowseRejectsDisabledOrCrossDomainResponse()
  {
    var objectId = Guid.NewGuid();
    var handler = new StubHandler(_ => Json($$"""{"value":[{"id":"{{objectId}}","displayName":"Ada","userPrincipalName":"ada@other.test","accountEnabled":true,"userType":"Member"}]}"""));
    var reader = new GraphDirectoryReader(new HttpClient(handler), new StubTokenSource(
      new("opaque", Tenant, new HashSet<string> { "User.Read.All" })), Options);
    await Assert.ThrowsAsync<OperationBlockedException>(() =>
      reader.BrowseActiveAsync(Tenant, "example.test", null, default));
  }

  [Fact]
  public async Task WrongRoleOrTenantNeverCallsGraph()
  {
    var handler = new StubHandler(_ => throw new Xunit.Sdk.XunitException("Graph must not be called"));
    var reader = new GraphDirectoryReader(new HttpClient(handler), new StubTokenSource(
      new("opaque", Tenant, new HashSet<string> { "Sites.Selected" })), Options);
    await Assert.ThrowsAsync<OperationBlockedException>(() => reader.SearchAsync(Tenant, "Ad", null, default));
    await Assert.ThrowsAsync<OperationBlockedException>(() => reader.SearchAsync(Guid.NewGuid().ToString("D"), "Ad", null, default));
  }

  [Fact]
  public async Task CrossHostNextLinkFailsClosed()
  {
    var handler = new StubHandler(_ => Json("""{"value":[],"@odata.nextLink":"https://evil.example/v1.0/users?$skiptoken=stolen"}"""));
    var reader = new GraphDirectoryReader(new HttpClient(handler), new StubTokenSource(
      new("opaque", Tenant, new HashSet<string> { "User.Read.All" })), Options);
    await Assert.ThrowsAsync<OperationBlockedException>(() => reader.SearchAsync(Tenant, "Ad", null, default));
  }

  [Fact]
  public async Task DisabledReaderCannotCallGraph()
  {
    var handler = new StubHandler(_ => throw new Xunit.Sdk.XunitException("Graph must not be called"));
    var options = Options with { Enabled = false };
    var reader = new GraphDirectoryReader(new HttpClient(handler), new StubTokenSource(
      new("opaque", Tenant, new HashSet<string> { "User.Read.All" })), options);
    await Assert.ThrowsAsync<OperationBlockedException>(() => reader.SearchAsync(Tenant, "Ad", null, default));
  }

  [Fact]
  public async Task ExactObjectReadRejectsMismatchedGraphIdentity()
  {
    var requestedId = Guid.NewGuid();
    var wrongId = Guid.NewGuid();
    Uri? requested = null;
    var handler = new StubHandler(request =>
    {
      requested = request.RequestUri;
      return Json($$"""{"id":"{{wrongId}}","displayName":"Other","userPrincipalName":"other@example.test","accountEnabled":true,"userType":"Member"}""");
    });
    var reader = new GraphDirectoryReader(new HttpClient(handler), new StubTokenSource(
      new("opaque", Tenant, new HashSet<string> { "User.Read.All" })), Options);
    await Assert.ThrowsAsync<OperationBlockedException>(() =>
      reader.GetByIdAsync(Tenant, requestedId.ToString("D"), default));
    Assert.Equal($"/v1.0/users/{requestedId:D}", requested?.AbsolutePath);
  }

  [Fact]
  public async Task ExactObjectReadReturnsEnabledStatusForImmutableIdentity()
  {
    var objectId = Guid.NewGuid();
    var handler = new StubHandler(_ => Json($$"""{"id":"{{objectId}}","displayName":"Ada","userPrincipalName":"ada@example.test","accountEnabled":false,"userType":"Member"}"""));
    var reader = new GraphDirectoryReader(new HttpClient(handler), new StubTokenSource(
      new("opaque", Tenant, new HashSet<string> { "User.Read.All" })), Options);
    var candidate = await reader.GetByIdAsync(Tenant, objectId.ToString("D"), default);
    Assert.Equal(objectId.ToString("D"), candidate.ObjectId);
    Assert.False(candidate.AccountEnabled);
  }

  [Theory]
  [InlineData("browse")]
  [InlineData("search")]
  [InlineData("id")]
  public async Task GraphResponsesEnforceByteBoundForHeaderlessAndMisleadingLengths(string method)
  {
    var objectId = Guid.NewGuid();
    var exactStream = new TrackingReadStream(Payload(method, 262144, objectId));
    var exactReader = Reader(exactStream);
    await InvokeAsync(exactReader, method, objectId, default);
    Assert.Equal(262144, exactStream.BytesRead);
    Assert.True(exactStream.IsDisposed);

    foreach (var declaredLength in new long?[] { null, 1 })
    {
      var oversizedStream = new TrackingReadStream(Payload(method, 262145, objectId));
      var oversizedReader = Reader(oversizedStream, declaredLength);
      var returned = false;
      var exception = await Assert.ThrowsAsync<OperationBlockedException>(async () =>
      {
        await InvokeAsync(oversizedReader, method, objectId, default);
        returned = true;
      });
      Assert.Equal("directory-reader-graph-rejected", exception.Message);
      Assert.False(returned);
      Assert.Equal(262145, oversizedStream.BytesRead);
      Assert.True(oversizedStream.IsDisposed);
    }
  }

  [Theory]
  [InlineData("browse")]
  [InlineData("search")]
  [InlineData("id")]
  public async Task GraphResponseReadPropagatesCancellation(string method)
  {
    using var cts = new CancellationTokenSource();
    var objectId = Guid.NewGuid();
    var stream = new TrackingReadStream(Payload(method, 1024, objectId), cts);
    var reader = Reader(stream);
    var returned = false;
    await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
    {
      await InvokeAsync(reader, method, objectId, cts.Token);
      returned = true;
    });
    Assert.False(returned);
    Assert.True(cts.IsCancellationRequested);
    Assert.True(stream.IsDisposed);
  }

  private static GraphDirectoryReader Reader(TrackingReadStream stream, long? declaredLength = null)
  {
    var handler = new StubHandler(_ =>
    {
      var content = new StreamContent(stream);
      content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
      if (declaredLength is not null) content.Headers.ContentLength = declaredLength;
      Assert.Equal(declaredLength, content.Headers.ContentLength);
      return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    });
    return new GraphDirectoryReader(new HttpClient(handler), new StubTokenSource(
      new("opaque", Tenant, new HashSet<string> { "User.Read.All" })), Options);
  }

  private static byte[] Payload(string method, int length, Guid objectId)
  {
    var prefix = method == "id"
      ? "{\"id\":\"" + objectId.ToString("D") + "\",\"displayName\":\"Ada\",\"userPrincipalName\":\"ada@example.test\",\"accountEnabled\":true,\"userType\":\"Member\",\"padding\":\""
      : "{\"value\":[],\"padding\":\"";
    const string suffix = "\"}";
    var paddingLength = length - Encoding.UTF8.GetByteCount(prefix) - Encoding.UTF8.GetByteCount(suffix);
    if (paddingLength < 0) throw new ArgumentOutOfRangeException(nameof(length));
    return Encoding.UTF8.GetBytes(prefix + new string('x', paddingLength) + suffix);
  }

  private static async Task InvokeAsync(GraphDirectoryReader reader, string method, Guid objectId,
    CancellationToken ct)
  {
    switch (method)
    {
      case "browse":
        await reader.BrowseActiveAsync(Tenant, "example.test", null, ct);
        return;
      case "search":
        await reader.SearchAsync(Tenant, "Ada", null, ct, "example.test");
        return;
      case "id":
        await reader.GetByIdAsync(Tenant, objectId.ToString("D"), ct);
        return;
      default:
        throw new ArgumentOutOfRangeException(nameof(method));
    }
  }

  private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
  {
    Content = new StringContent(value, Encoding.UTF8, "application/json")
  };

  private sealed class StubTokenSource(DirectoryToken token) : IDirectoryTokenSource
  {
    public Task<DirectoryToken> GetAsync(string tenantId, CancellationToken ct) => Task.FromResult(token);
  }

  private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
      Task.FromResult(send(request));
  }

  private sealed class TrackingReadStream(byte[] content, CancellationTokenSource? cancelOnRead = null) : Stream
  {
    private int position;
    public int BytesRead { get; private set; }
    public bool IsDisposed { get; private set; }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
      var count = Math.Min(buffer.Length, content.Length - position);
      content.AsSpan(position, count).CopyTo(buffer);
      position += count;
      BytesRead += count;
      return count;
    }
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
      if (cancelOnRead is not null)
      {
        cancelOnRead.Cancel();
        cancellationToken.ThrowIfCancellationRequested();
      }
      cancellationToken.ThrowIfCancellationRequested();
      return ValueTask.FromResult(Read(buffer.Span));
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
      ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    protected override void Dispose(bool disposing)
    {
      IsDisposed = true;
      base.Dispose(disposing);
    }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
  }
}
