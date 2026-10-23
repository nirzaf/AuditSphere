using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using AuditSphereOps.Api.Authentication;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AuditSphereOps.Api.Tests;

/// <summary>HTTP and PostgreSQL coverage for the public, deployment-bound legacy mail evidence enrollment.</summary>
public sealed class MailAuthorizationEvidenceApiTests
{
  private const string Path = "/api/ui/administration/microsoft365/mail-authorization-evidence/enroll";
  private const string Sha256Oid = "2.16.840.1.101.3.4.2.1";
  private const string BuildCommit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
  private const string PlanSha256 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

  [Fact]
  public async Task LegacyEvidenceEnrollment_RequiresCsrfAndExactScope_PersistsReceiptAndProjectsStatus_AndReplayCreatesOneRow()
  {
    await using var fixture = await TenantAdministrationFixture.CreateAsync();
    await fixture.ConnectAndVerifyAsync();
    var now = DateTimeOffset.UtcNow;
    Guid consentAttemptId;
    await using (var db = fixture.Db())
      consentAttemptId = await db.TenantConsentAttempts.AsNoTracking()
        .Where(x => x.State == TenantConsentAttemptStates.ConsentVerified)
        .Select(x => x.Id).SingleAsync();

    using var signer = CreateSyntheticSigner(now);
    var options = RuntimeOptions(fixture, signer);
    var evidence = CreateEvidence(fixture, consentAttemptId, signer, now, Guid.Parse(fixture.TenantId));
    var body = Request(evidence);
    var staff = TenantAdministrationFixture.User(fixture.FirmId, fixture.TenantId,
      "mail-staff@example.test", "Mail Evidence Staff");
    await using (var db = fixture.Db())
    {
      db.Users.Add(staff);
      db.RoleGrants.Add(new RoleGrant
      {
        Id = Guid.CreateVersion7(), FirmId = fixture.FirmId, UserId = staff.Id, Role = "Staff",
        GrantedAt = now, GrantedByUserId = fixture.Admin.Id
      });
      await db.SaveChangesAsync();
    }

    using (var staffFactory = Factory(fixture, options, now, staff.Subject))
    using (var staffClient = staffFactory.CreateClient(new() { AllowAutoRedirect = false }))
    {
      var staffCsrf = await SignIn(staffClient);
      using var denied = await Post(staffClient, staffCsrf, body);
      Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    using var factory = Factory(fixture, options, now);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    var csrf = await SignIn(client);

    using (var missingCsrf = await client.PostAsJsonAsync(Path, body))
      Assert.Equal(HttpStatusCode.Forbidden, missingCsrf.StatusCode);

    var wrongScope = CreateEvidence(fixture, consentAttemptId, signer, now, Guid.NewGuid());
    using (var mismatch = await Post(client, csrf, Request(wrongScope)))
      Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);

    using (var oversized = await Post(client, csrf, new
    {
      canonicalPayloadBase64 = new string('A', 1_398_105),
      detachedCmsBase64 = Convert.ToBase64String(evidence.Cms),
      reviewed = true
    }))
      Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);

    await using (var db = fixture.Db())
    {
      Assert.Empty(await db.TenantCapabilityVerifications.Where(x => x.FirmId == fixture.FirmId &&
        x.Capability == Microsoft365Capabilities.OutboundMail).ToListAsync());
      Assert.Empty(await db.Microsoft365AdministrationEvents.Where(x => x.FirmId == fixture.FirmId &&
        x.Operation == "MAIL_AUTHORIZATION_EVIDENCE_ENROLLED").ToListAsync());
    }

    using var enrolled = await Post(client, csrf, body);
    Assert.Equal(HttpStatusCode.OK, enrolled.StatusCode);
    var receiptId = (await Value(enrolled)).GetGuid();

    using (var workspace = await client.GetAsync("/api/ui/administration/microsoft365"))
    {
      Assert.Equal(HttpStatusCode.OK, workspace.StatusCode);
      var statusBody = await workspace.Content.ReadAsStringAsync();
      using var json = JsonDocument.Parse(statusBody);
      var mail = json.RootElement.GetProperty("workspace").GetProperty("capabilities")
        .EnumerateArray().Single(x => x.GetProperty("capability").GetString() == Microsoft365Capabilities.OutboundMail);
      Assert.Equal(CapabilityVerificationStates.Verified, mail.GetProperty("state").GetString());
      Assert.Equal(receiptId, mail.GetProperty("authorizationEvidence").GetProperty("receiptId").GetGuid());
      Assert.False(mail.GetProperty("authorizationEvidence").TryGetProperty("canonicalPayload", out _));
      Assert.DoesNotContain(Convert.ToBase64String(evidence.Payload), statusBody, StringComparison.Ordinal);
      Assert.DoesNotContain(Convert.ToBase64String(evidence.Cms), statusBody, StringComparison.Ordinal);
    }

    using var replay = await Post(client, csrf, body);
    Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
    Assert.Equal(receiptId, (await Value(replay)).GetGuid());

    await using var final = fixture.Db();
    var row = await final.TenantCapabilityVerifications.SingleAsync(x => x.FirmId == fixture.FirmId &&
      x.Capability == Microsoft365Capabilities.OutboundMail);
    Assert.Equal(receiptId, row.Id);
    Assert.Equal(MailAuthorizationEvidenceEnrollment.LegacyModel, row.MailAuthorizationModel);
    Assert.Equal(evidence.Payload, row.MailCanonicalPayload);
    Assert.Equal(evidence.Cms, row.MailDetachedCms);
    Assert.Equal(1, await final.Microsoft365AdministrationEvents.CountAsync(x => x.FirmId == fixture.FirmId &&
      x.Operation == "MAIL_AUTHORIZATION_EVIDENCE_ENROLLED"));
  }

  private static object Request(SignedEvidence evidence) => new
  {
    canonicalPayloadBase64 = Convert.ToBase64String(evidence.Payload),
    detachedCmsBase64 = Convert.ToBase64String(evidence.Cms),
    reviewed = true
  };

  private static MailAuthorizationRuntimeOptions RuntimeOptions(TenantAdministrationFixture fixture,
    X509Certificate2 signer)
  {
    var source = Source();
    return new MailAuthorizationRuntimeOptions(fixture.FirmId, Guid.Parse(fixture.TenantId),
      MailAuthorizationModel.LegacyApplicationAccessPolicy, RuntimeClientId, EnterpriseServicePrincipalObjectId,
      [SenderMailboxObjectId], ControlMailboxObjectId, PlanSha256, source, BuildCommit,
      Convert.ToHexString(signer.GetCertHash(HashAlgorithmName.SHA256)).ToLowerInvariant(), TimeSpan.FromHours(24));
  }

  private static SignedEvidence CreateEvidence(TenantAdministrationFixture fixture, Guid consentAttemptId,
    X509Certificate2 signer, DateTimeOffset now, Guid attestedTenantId)
  {
    var source = Source();
    var snapshot = new MailAuthorizationProviderSnapshot(attestedTenantId,
      MailAuthorizationModel.LegacyApplicationAccessPolicy, RuntimeClientId, EnterpriseServicePrincipalObjectId,
      GraphServicePrincipalObjectId, GraphServicePrincipalAppId, GraphMailSendAppRoleId, "Mail.Send", true,
      ["Application"], [SenderMailboxObjectId], ControlMailboxObjectId,
      [new(Guid.NewGuid(), EnterpriseServicePrincipalObjectId, GraphServicePrincipalObjectId, GraphMailSendAppRoleId)],
      [], new(Guid.NewGuid(), "Synthetic Sender Group", "Synthetic Sender Group", "RestrictAccess",
        [SenderMailboxObjectId], [RuntimeClientId],
        [new(SenderMailboxObjectId, true), new(ControlMailboxObjectId, false)]), null);
    var attestation = MailAuthorizationAttestationCanonicalizer.Create(fixture.ConnectionId, consentAttemptId,
      PlanSha256, source, now.AddMinutes(-1), now.AddHours(1), snapshot);
    var payload = MailAuthorizationAttestationCanonicalizer.SerializeCanonical(attestation);
    return new(payload, SignDetached(payload, signer));
  }

  private static MailAuthorizationSourceIdentity Source() => new(BuildCommit,
    new string('c', 64), new string('d', 64), new string('e', 64), new string('f', 64),
    new string('1', 64), new string('2', 64));

  private static X509Certificate2 CreateSyntheticSigner(DateTimeOffset now)
  {
    using var rsa = RSA.Create(2048);
    var request = new CertificateRequest("CN=AuditSphere Synthetic Mail Attestation API Test", rsa,
      HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
    return request.CreateSelfSigned(now.AddDays(-1), now.AddDays(30));
  }

  private static byte[] SignDetached(byte[] payload, X509Certificate2 signer)
  {
    var cms = new SignedCms(new ContentInfo(payload), detached: true);
    cms.ComputeSignature(new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, signer)
    {
      DigestAlgorithm = new Oid(Sha256Oid),
      IncludeOption = X509IncludeOption.EndCertOnly
    }, silent: true);
    return cms.Encode();
  }

  private static StandaloneApiApplicationFactory Factory(TenantAdministrationFixture fixture,
    MailAuthorizationRuntimeOptions runtime, DateTimeOffset now, string? subject = null)
  {
    var options = new TenantAdministrationOptions(fixture.TenantId, OutboundMailEnabled: true,
      VerificationMaxAge: TimeSpan.FromHours(24), MailAuthorization: runtime, MailEvidenceEnrollmentEnabled: true);
    var settings = new TenantAdministrationSettings(options, Simulation: true, ConsentEnabled: false, MailSender: null);
    return new(new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = fixture.Pg.ConnectionString,
      ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:TenantId"] = fixture.TenantId,
      ["DevelopmentIdentity:Subject"] = subject ?? fixture.Admin.Subject,
      ["Application:AllowSimulationAdapters"] = "true",
      ["ExternalEffects:Enabled"] = "false",
      ["TenantAdministration:Simulation:Enabled"] = "true"
    }, services =>
    {
      services.RemoveAll<TenantAdministrationSettings>();
      services.AddSingleton(settings);
      services.RemoveAll<TimeProvider>();
      services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));
    });
  }

  private static async Task<string> SignIn(HttpClient client)
  {
    await client.GetAsync("/auth/sign-in");
    using var session = await client.GetAsync("/api/ui/session");
    Assert.Equal(HttpStatusCode.OK, session.StatusCode);
    return Uri.UnescapeDataString(session.Headers.GetValues("Set-Cookie").Single(x =>
      x.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);
  }

  private static Task<HttpResponseMessage> Post(HttpClient client, string csrf, object body)
  {
    var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = JsonContent.Create(body) };
    request.Headers.Add("X-XSRF-TOKEN", csrf);
    return client.SendAsync(request);
  }

  private static async Task<JsonElement> Value(HttpResponseMessage response)
  {
    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return json.RootElement.GetProperty("value").Clone();
  }

  private static readonly Guid RuntimeClientId = Guid.Parse("10000000-0000-4000-8000-000000000001");
  private static readonly Guid EnterpriseServicePrincipalObjectId = Guid.Parse("10000000-0000-4000-8000-000000000002");
  private static readonly Guid GraphServicePrincipalObjectId = Guid.Parse("00000003-0000-0000-c000-000000000003");
  private static readonly Guid GraphServicePrincipalAppId = Guid.Parse("00000003-0000-0000-c000-000000000000");
  private static readonly Guid GraphMailSendAppRoleId = Guid.Parse("10000000-0000-4000-8000-000000000004");
  private static readonly Guid SenderMailboxObjectId = Guid.Parse("10000000-0000-4000-8000-000000000005");
  private static readonly Guid ControlMailboxObjectId = Guid.Parse("10000000-0000-4000-8000-000000000006");

  private sealed record SignedEvidence(byte[] Payload, byte[] Cms);

  private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
  {
    public override DateTimeOffset GetUtcNow() => utcNow;
  }
}
