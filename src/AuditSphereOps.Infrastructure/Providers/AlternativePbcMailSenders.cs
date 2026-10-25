using System.Net;
using System.Net.Mail;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;

namespace AuditSphereOps.Infrastructure.Providers;

public sealed record SmtpMailOptions(
  string Host, int Port, bool UseSsl, string SenderAddress, string? SenderName,
  string? Username, string? Password)
{
  public void Validate()
  {
    if (string.IsNullOrWhiteSpace(Host) || Port is < 1 or > 65535 ||
        !MailAddress.TryCreate(SenderAddress, out _) ||
        (string.IsNullOrWhiteSpace(Username) != string.IsNullOrWhiteSpace(Password)))
      throw new InvalidOperationException("SmtpMail requires a host, port, sender and optional username/password pair.");
  }
}

public sealed class SmtpPbcMailSender(SmtpMailOptions options) : IPbcMailSender
{
  public async Task SendAsync(PbcMailPlan plan, CancellationToken ct)
  {
    options.Validate();
    using var message = new MailMessage
    {
      From = new MailAddress(options.SenderAddress, options.SenderName),
      Subject = plan.Subject,
      Body = plan.Body,
      IsBodyHtml = false
    };
    message.To.Add(plan.Recipient);
    using var smtp = new SmtpClient(options.Host, options.Port) { EnableSsl = options.UseSsl };
    if (!string.IsNullOrWhiteSpace(options.Username))
      smtp.Credentials = new NetworkCredential(options.Username, options.Password);
    await smtp.SendMailAsync(message, ct);
  }
}

public sealed record ResendMailOptions(string ApiKey, string SenderAddress)
{
  public void Validate()
  {
    if (string.IsNullOrWhiteSpace(ApiKey) || !MailAddress.TryCreate(SenderAddress, out _))
      throw new InvalidOperationException("ResendMail requires an API key and verified sender address.");
  }
}

public sealed class ResendPbcMailSender(HttpClient http, ResendMailOptions options) : IPbcMailSender
{
  public async Task SendAsync(PbcMailPlan plan, CancellationToken ct)
  {
    options.Validate();
    using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
    request.Headers.TryAddWithoutValidation("Idempotency-Key", plan.CorrelationId.ToString("D"));
    request.Content = JsonContent.Create(new
    {
      from = options.SenderAddress,
      to = new[] { plan.Recipient },
      subject = plan.Subject,
      text = plan.Body
    });
    using var response = await http.SendAsync(request, ct);
    if (response.IsSuccessStatusCode) return;
    if (response.StatusCode == HttpStatusCode.TooManyRequests)
      throw new SafeRetryException(response.Headers.RetryAfter?.Delta);
    if ((int)response.StatusCode is >= 400 and < 500)
      throw new OperationBlockedException("resend-mail-rejected", authorization: response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
    throw new HttpRequestException("Resend mail outcome was not accepted.");
  }
}

/// <summary>
/// A useSend installation's REST API. The sender must be an alias the installation approves for the API key; the key never
/// appears in this record's text form. HTTPS only, with no credentials, query or fragment in the base URL.
/// </summary>
public sealed record UseSendMailOptions(string BaseUrl, string ApiKey, string SenderAddress)
{
  public Uri SendEndpoint
  {
    get
    {
      Validate();
      return new Uri(BaseUrl.TrimEnd('/') + "/v1/emails");
    }
  }

  public void Validate()
  {
    if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps ||
        string.IsNullOrEmpty(url.Host) || !string.IsNullOrEmpty(url.UserInfo) || !string.IsNullOrEmpty(url.Query) ||
        !string.IsNullOrEmpty(url.Fragment) || string.IsNullOrWhiteSpace(ApiKey) ||
        !MailAddress.TryCreate(SenderAddress, out _))
      throw new InvalidOperationException(
        "UseSendMail requires an https base URL without credentials, query or fragment, an API key and an approved sender address.");
  }

  public override string ToString() => $"UseSendMailOptions {{ BaseUrl = {BaseUrl}, SenderAddress = {SenderAddress}, ApiKey = [redacted] }}";
}

/// <summary>
/// Sends the plain-text message through a useSend installation. useSend reports SENT once it has submitted the message to
/// its own Microsoft connection; that is not delivery evidence. An attachment is refused rather than dropped, so a document
/// that must travel with the message is never omitted.
/// </summary>
public sealed class UseSendPbcMailSender(HttpClient http, UseSendMailOptions options) : IPbcMailSender
{
  public async Task SendAsync(PbcMailPlan plan, CancellationToken ct)
  {
    var endpoint = options.SendEndpoint;
    if (plan.Attachment is not null) throw new OperationBlockedException("usesend-attachment-unsupported");
    using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
    request.Headers.TryAddWithoutValidation("Idempotency-Key", "auditsphere-mail-" + plan.CorrelationId.ToString("D"));
    request.Content = JsonContent.Create(new
    {
      from = options.SenderAddress,
      to = plan.Recipient,
      subject = plan.Subject,
      text = plan.Body
    });
    using var response = await http.SendAsync(request, ct);
    if (response.IsSuccessStatusCode)
    {
      // An accepted message returns its email ID; a success without one is an unconfirmed outcome, never a silent pass.
      try
      {
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (body.RootElement.ValueKind == JsonValueKind.Object &&
            body.RootElement.TryGetProperty("emailId", out var id) && id.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(id.GetString())) return;
      }
      catch (JsonException) { }
      throw new HttpRequestException("UseSend mail outcome was not confirmed.");
    }
    if (response.StatusCode == HttpStatusCode.TooManyRequests)
      throw new SafeRetryException(response.Headers.RetryAfter?.Delta);
    if ((int)response.StatusCode is >= 400 and < 500)
      throw new OperationBlockedException("usesend-mail-rejected",
        authorization: response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
    throw new HttpRequestException("UseSend mail outcome was not accepted.");
  }
}
