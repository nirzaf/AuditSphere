using System.Net;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Infrastructure.Providers;

namespace AuditSphereOps.Domain.Tests;

public sealed class UseSendPbcMailSenderTests
{
  private const string Key = "us_synthetic_test_key_not_a_secret";
  private static readonly UseSendMailOptions Options = new("https://mail.example.test/api/", Key, "mail@steaudit.example");

  private sealed class StubHandler(HttpStatusCode status, string body = "{\"emailId\":\"email_1\"}", TimeSpan? retryAfter = null)
    : HttpMessageHandler
  {
    public int Calls { get; private set; }
    public HttpRequestMessage? Request { get; private set; }
    public string? Payload { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
      Calls++;
      Request = request;
      Payload = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
      var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
      if (retryAfter is { } delay) response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(delay);
      return response;
    }
  }

  private static PbcMailPlan Plan(PbcMailAttachment? attachment = null) =>
    new("recipient@example.test", "Reviewed proposal", "Please review.", Guid.Parse("11111111-2222-4333-8444-555555555555"), attachment);

  [Fact]
  public async Task Send_PostsTheExactMessageWithBearerKeyAndStableIdempotencyKey()
  {
    var handler = new StubHandler(HttpStatusCode.OK);
    using var http = new HttpClient(handler);

    await new UseSendPbcMailSender(http, Options).SendAsync(Plan(), CancellationToken.None);

    Assert.Equal(1, handler.Calls);
    Assert.Equal(HttpMethod.Post, handler.Request!.Method);
    Assert.Equal("https://mail.example.test/api/v1/emails", handler.Request.RequestUri!.ToString());
    Assert.Equal(("Bearer", Key), (handler.Request.Headers.Authorization!.Scheme, handler.Request.Headers.Authorization.Parameter));
    Assert.Equal("auditsphere-mail-11111111-2222-4333-8444-555555555555", Assert.Single(handler.Request.Headers.GetValues("Idempotency-Key")));
    using var payload = JsonDocument.Parse(handler.Payload!);
    Assert.Equal(("mail@steaudit.example", "recipient@example.test", "Reviewed proposal", "Please review."),
      (payload.RootElement.GetProperty("from").GetString(), payload.RootElement.GetProperty("to").GetString(),
        payload.RootElement.GetProperty("subject").GetString(), payload.RootElement.GetProperty("text").GetString()));
  }

  [Fact]
  public async Task Send_RefusesAnAttachmentInsteadOfDroppingItAndMakesNoRequest()
  {
    var handler = new StubHandler(HttpStatusCode.OK);
    using var http = new HttpClient(handler);
    var attachment = new PbcMailAttachment("Offer.docx", "application/octet-stream", [1, 2, 3]);

    var blocked = await Assert.ThrowsAsync<OperationBlockedException>(() =>
      new UseSendPbcMailSender(http, Options).SendAsync(Plan(attachment), CancellationToken.None));

    Assert.Equal(("usesend-attachment-unsupported", false), (blocked.Code, blocked.Authorization));
    Assert.Equal(0, handler.Calls);
  }

  [Theory]
  [InlineData(HttpStatusCode.Unauthorized, true)]
  [InlineData(HttpStatusCode.Forbidden, true)]
  [InlineData(HttpStatusCode.BadRequest, false)]
  public async Task Send_ClassifiesAProviderRefusalAsBlockedWithoutRetrying(HttpStatusCode status, bool authorization)
  {
    var handler = new StubHandler(status, "{\"error\":\"synthetic\"}");
    using var http = new HttpClient(handler);

    var blocked = await Assert.ThrowsAsync<OperationBlockedException>(() =>
      new UseSendPbcMailSender(http, Options).SendAsync(Plan(), CancellationToken.None));

    Assert.Equal(("usesend-mail-rejected", authorization), (blocked.Code, blocked.Authorization));
    Assert.Equal(1, handler.Calls);
  }

  [Fact]
  public async Task Send_RateLimitIsSafeToRetryButAnUnconfirmedAcceptanceIsNot()
  {
    using var limited = new HttpClient(new StubHandler(HttpStatusCode.TooManyRequests, "{}", TimeSpan.FromSeconds(7)));
    var retry = await Assert.ThrowsAsync<SafeRetryException>(() =>
      new UseSendPbcMailSender(limited, Options).SendAsync(Plan(), CancellationToken.None));
    Assert.Equal(TimeSpan.FromSeconds(7), retry.RetryAfter);

    foreach (var (status, body) in new[]
    {
      (HttpStatusCode.InternalServerError, "{}"), (HttpStatusCode.OK, "{}"), (HttpStatusCode.OK, "not json"),
      (HttpStatusCode.OK, "{\"emailId\":\"\"}")
    })
    {
      using var http = new HttpClient(new StubHandler(status, body));
      await Assert.ThrowsAsync<HttpRequestException>(() =>
        new UseSendPbcMailSender(http, Options).SendAsync(Plan(), CancellationToken.None));
    }
  }

  [Theory]
  [InlineData("http://mail.example.test/api", Key, "mail@steaudit.example")]
  [InlineData("https://user:pass@mail.example.test/api", Key, "mail@steaudit.example")]
  [InlineData("https://mail.example.test/api?x=1", Key, "mail@steaudit.example")]
  [InlineData("https://mail.example.test/api#frag", Key, "mail@steaudit.example")]
  [InlineData("not a url", Key, "mail@steaudit.example")]
  [InlineData("https://mail.example.test/api", " ", "mail@steaudit.example")]
  [InlineData("https://mail.example.test/api", Key, "not-an-address")]
  public async Task Options_RefuseAnUnsafeEndpointMissingKeyOrInvalidSender(string baseUrl, string key, string sender)
  {
    var options = new UseSendMailOptions(baseUrl, key, sender);
    Assert.Throws<InvalidOperationException>(options.Validate);
    var handler = new StubHandler(HttpStatusCode.OK);
    using var http = new HttpClient(handler);
    await Assert.ThrowsAsync<InvalidOperationException>(() =>
      new UseSendPbcMailSender(http, options).SendAsync(Plan(), CancellationToken.None));
    Assert.Equal(0, handler.Calls);
  }

  [Fact]
  public void Options_NeverPrintTheApiKey()
  {
    Assert.DoesNotContain(Key, Options.ToString());
    Assert.Contains("[redacted]", Options.ToString());
  }
}
