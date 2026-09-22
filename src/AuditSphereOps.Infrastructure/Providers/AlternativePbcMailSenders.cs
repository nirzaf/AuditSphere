using System.Net;
using System.Net.Mail;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
