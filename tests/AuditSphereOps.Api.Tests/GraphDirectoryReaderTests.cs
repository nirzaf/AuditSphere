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
}
