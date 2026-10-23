using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// Synthetic offline coverage. The ephemeral signer exists in process memory; no live tenant, provider call,
/// configured signer, app grant, consent change, or production private key is involved.
/// </summary>
public sealed class MailAuthorizationAttestationTests
{
  private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
  private const string Sha256Oid = "2.16.840.1.101.3.4.2.1";

  [Theory]
  [InlineData(MailAuthorizationModel.LegacyApplicationAccessPolicy)]
  [InlineData(MailAuthorizationModel.ExchangeRbacResourceScope)]
  public void DetachedPinnedCms_VerifiesCanonicalNormalizedProviderSnapshot(MailAuthorizationModel model)
  {
    using var signer = CreateSyntheticSigner(Now);
    var snapshot = CreateSnapshot(model);
    var attestation = CreateAttestation(snapshot, Now, Now.AddHours(1));
    var payload = MailAuthorizationAttestationCanonicalizer.SerializeCanonical(attestation);
    var signature = SignDetached(payload, signer);

    var verified = MailAuthorizationAttestationVerifier.VerifyBound(payload, signature,
      Options(signer, TimeSpan.FromHours(24)), ExpectedBinding(attestation), Now);

    Assert.Equal(model, verified.ProviderSnapshot.PermissionModel);
    Assert.Equal(snapshot.TenantId, verified.ProviderSnapshot.TenantId);
    Assert.Equal(attestation.ConnectionRevisionId, verified.ConnectionRevisionId);
    Assert.Equal(attestation.ConsentAttemptId, verified.ConsentAttemptId);
    Assert.Equal(attestation.PlanSha256, verified.PlanSha256);
    Assert.Equal(attestation.Source, verified.Source);
    Assert.Equal(new string('e', 64), verified.Source.CommonModuleSha256);
    Assert.Equal(new string('f', 64), verified.Source.EntraModuleSha256);
    Assert.Equal(new string('1', 64), verified.Source.PlanSchemaSha256);
    Assert.Equal(snapshot.SenderMailboxObjectIds.OrderBy(x => x).ToArray(),
      verified.ProviderSnapshot.SenderMailboxObjectIds.OrderBy(x => x).ToArray());
    Assert.Equal(MailAuthorizationAttestationCanonicalizer.ComputeProviderSnapshotSha256(snapshot),
      verified.ProviderSnapshotSha256);
    Assert.NotEqual(verified.PlanSha256, verified.ProviderSnapshotSha256);
    if (model == MailAuthorizationModel.LegacyApplicationAccessPolicy)
    {
      Assert.Empty(verified.ProviderSnapshot.ExchangeRoleAssignmentSet);
      Assert.False(verified.ProviderSnapshot.LegacyAuthorization!.Probes.Single(
        x => x.MailboxObjectId == verified.ProviderSnapshot.ControlMailboxObjectId).Allowed);
    }
    else
    {
      Assert.Equal("Application Mail.Send", Assert.Single(verified.ProviderSnapshot.ExchangeRoleAssignmentSet).Role);
      Assert.False(verified.ProviderSnapshot.ExchangeRbacAuthorization!.Probes.Single(
        x => x.MailboxObjectId == verified.ProviderSnapshot.ControlMailboxObjectId).InScope);
    }
  }

  [Fact]
  public void SignatureOverDifferentPayload_IsRejected()
  {
    using var signer = CreateSyntheticSigner(Now);
    var payload = MailAuthorizationAttestationCanonicalizer.SerializeCanonical(
      CreateAttestation(CreateSnapshot(MailAuthorizationModel.ExchangeRbacResourceScope), Now, Now.AddHours(1)));
    var signature = SignDetached(payload, signer);
    var tampered = payload.ToArray();
    var tamperIndex = Array.IndexOf(tampered, (byte)'S');
    Assert.True(tamperIndex >= 0);
    tampered[tamperIndex] ^= 1;

    AssertCode("MAIL_ATTESTATION_SIGNATURE_INVALID", () => MailAuthorizationAttestationVerifier.Verify(
      tampered, signature, Options(signer, TimeSpan.FromHours(24)), Now));
  }

  [Theory]
  [InlineData("unknown")]
  [InlineData("duplicate")]
  public void SignedPayloadWithUnknownOrDuplicateProperties_IsRejected(string mutation)
  {
    using var signer = CreateSyntheticSigner(Now);
    var payload = Encoding.UTF8.GetString(MailAuthorizationAttestationCanonicalizer.SerializeCanonical(
      CreateAttestation(CreateSnapshot(MailAuthorizationModel.LegacyApplicationAccessPolicy), Now, Now.AddHours(1))));
    var malformed = mutation == "unknown"
      ? payload.Insert(1, "\"unapprovedField\":true,")
      : payload.Replace("{\"schemaVersion\":1,", "{\"schemaVersion\":1,\"schemaVersion\":1,", StringComparison.Ordinal);
    Assert.NotEqual(payload, malformed);
    var bytes = Encoding.UTF8.GetBytes(malformed);

    AssertCode(MailAuthorizationAttestationCanonicalizer.InvalidCode, () => MailAuthorizationAttestationVerifier.Verify(
      bytes, SignDetached(bytes, signer), Options(signer, TimeSpan.FromHours(24)), Now));
  }

  [Fact]
  public void DifferentConfiguredSignerFingerprint_IsRejected()
  {
    using var signer = CreateSyntheticSigner(Now);
    using var other = CreateSyntheticSigner(Now);
    var payload = MailAuthorizationAttestationCanonicalizer.SerializeCanonical(
      CreateAttestation(CreateSnapshot(MailAuthorizationModel.ExchangeRbacResourceScope), Now, Now.AddHours(1)));

    AssertCode("MAIL_ATTESTATION_SIGNER_UNTRUSTED", () => MailAuthorizationAttestationVerifier.Verify(
      payload, SignDetached(payload, signer), Options(other, TimeSpan.FromHours(24)), Now));
  }

  [Fact]
  public void AdditionalEmbeddedCertificate_IsRejectedWithoutBuildingTrustChain()
  {
    using var signer = CreateSyntheticSigner(Now);
    using var unrelated = CreateSyntheticSigner(Now);
    var payload = MailAuthorizationAttestationCanonicalizer.SerializeCanonical(
      CreateAttestation(CreateSnapshot(MailAuthorizationModel.LegacyApplicationAccessPolicy), Now, Now.AddHours(1)));
    var signature = SignDetached(payload, signer, unrelatedCertificate: unrelated);

    AssertCode("MAIL_ATTESTATION_SIGNATURE_INVALID", () => MailAuthorizationAttestationVerifier.Verify(
      payload, signature, Options(signer, TimeSpan.FromHours(24)), Now));
  }

  [Fact]
  public void AttachedCms_IsRejected()
  {
    using var signer = CreateSyntheticSigner(Now);
    var payload = MailAuthorizationAttestationCanonicalizer.SerializeCanonical(
      CreateAttestation(CreateSnapshot(MailAuthorizationModel.ExchangeRbacResourceScope), Now, Now.AddHours(1)));

    AssertCode("MAIL_ATTESTATION_SIGNATURE_INVALID", () => MailAuthorizationAttestationVerifier.Verify(
      payload, SignAttached(payload, signer), Options(signer, TimeSpan.FromHours(24)), Now));
  }

  [Theory]
  [InlineData(25, 1)]
  [InlineData(0, 25)]
  public void ExpiredOrOverlongEvidence_IsRejected(int observationAgeHours, int lifetimeHours)
  {
    using var signer = CreateSyntheticSigner(Now);
    var observed = Now.AddHours(-observationAgeHours);
    var expires = observed.AddHours(lifetimeHours);
    var payload = MailAuthorizationAttestationCanonicalizer.SerializeCanonical(
      CreateAttestation(CreateSnapshot(MailAuthorizationModel.ExchangeRbacResourceScope), observed, expires));

    AssertCode("MAIL_ATTESTATION_STALE", () => MailAuthorizationAttestationVerifier.Verify(
      payload, SignDetached(payload, signer), Options(signer, TimeSpan.FromHours(24)), Now));
  }

  [Fact]
  public void ControlMailboxCannotBeIncludedInSenderSet()
  {
    var snapshot = CreateSnapshot(MailAuthorizationModel.ExchangeRbacResourceScope);
    var invalid = snapshot with { ControlMailboxObjectId = snapshot.SenderMailboxObjectIds[0] };

    AssertCode(MailAuthorizationAttestationCanonicalizer.InvalidCode, () => CreateAttestation(invalid, Now, Now.AddHours(1)));
  }

  [Fact]
  public void ExchangeRbacReceipt_WithOverlappingEntraMailSendGrant_IsRejected()
  {
    var snapshot = CreateSnapshot(MailAuthorizationModel.ExchangeRbacResourceScope);
    var overlappingGrant = new MailEntraAppRoleAssignment(Guid.NewGuid(), snapshot.EnterpriseServicePrincipalObjectId,
      snapshot.GraphServicePrincipalObjectId, snapshot.GraphMailSendAppRoleId);

    AssertCode(MailAuthorizationAttestationCanonicalizer.InvalidCode, () =>
      MailAuthorizationAttestationCanonicalizer.Create(Guid.NewGuid(), Guid.NewGuid(), new string('a', 64),
        CreateAttestation(snapshot, Now, Now.AddHours(1)).Source, Now, Now.AddHours(1),
        snapshot with { EntraGrantSet = [overlappingGrant] }));
  }

  [Fact]
  public void GraphMailSendRole_WithNonApplicationMemberType_IsRejected()
  {
    var snapshot = CreateSnapshot(MailAuthorizationModel.LegacyApplicationAccessPolicy) with
    {
      GraphMailSendAllowedMemberTypes = ["Application", "User"]
    };

    AssertCode(MailAuthorizationAttestationCanonicalizer.InvalidCode, () =>
      MailAuthorizationAttestationCanonicalizer.Create(Guid.NewGuid(), Guid.NewGuid(), new string('a', 64),
        CreateAttestation(snapshot, Now, Now.AddHours(1)).Source, Now, Now.AddHours(1), snapshot));
  }

  [Fact]
  public void ProviderSnapshotMutationWithoutDigestUpdate_IsRejected()
  {
    var attestation = CreateAttestation(CreateSnapshot(MailAuthorizationModel.ExchangeRbacResourceScope), Now, Now.AddHours(1));
    var changedRbac = attestation.ProviderSnapshot.ExchangeRbacAuthorization! with
    {
      RecipientFilter = "A different synthetic filter."
    };
    var changed = attestation with
    {
      ProviderSnapshot = attestation.ProviderSnapshot with { ExchangeRbacAuthorization = changedRbac }
    };

    AssertCode(MailAuthorizationAttestationCanonicalizer.InvalidCode,
      () => MailAuthorizationAttestationCanonicalizer.SerializeCanonical(changed));
  }

  [Fact]
  public void SignedSourceDependencyDrift_IsRejectedAgainstServerExpectedBinding()
  {
    using var signer = CreateSyntheticSigner(Now);
    var original = CreateAttestation(CreateSnapshot(MailAuthorizationModel.ExchangeRbacResourceScope),
      Now, Now.AddHours(1));
    var changedSource = original.Source with { EntraModuleSha256 = new string('2', 64) };
    var changed = MailAuthorizationAttestationCanonicalizer.Create(
      original.ConnectionRevisionId, original.ConsentAttemptId, original.PlanSha256, changedSource,
      original.ObservedAtUtc, original.ExpiresAtUtc, original.ProviderSnapshot);
    var payload = MailAuthorizationAttestationCanonicalizer.SerializeCanonical(changed);
    var signature = SignDetached(payload, signer);

    AssertCode("MAIL_ATTESTATION_BINDING_MISMATCH", () => MailAuthorizationAttestationVerifier.VerifyBound(
      payload, signature, Options(signer, TimeSpan.FromHours(24)), ExpectedBinding(original), Now));
  }

  [Fact]
  public void SignedAttestationWithStaleConnectionConsentOrMailboxSet_IsRejectedAgainstCurrentBinding()
  {
    using var signer = CreateSyntheticSigner(Now);
    var attestation = CreateAttestation(CreateSnapshot(MailAuthorizationModel.LegacyApplicationAccessPolicy),
      Now, Now.AddHours(1));
    var payload = MailAuthorizationAttestationCanonicalizer.SerializeCanonical(attestation);
    var signature = SignDetached(payload, signer);
    var current = ExpectedBinding(attestation);
    var staleBindings = new[]
    {
      current with { ConnectionRevisionId = Guid.Parse("10000000-0000-4000-8000-000000000003") },
      current with { ConsentAttemptId = Guid.Parse("10000000-0000-4000-8000-000000000004") },
      current with { SenderMailboxObjectIds = [Guid.Parse("20000000-0000-4000-8000-000000000013")] },
      current with { ControlMailboxObjectId = Guid.Parse("20000000-0000-4000-8000-000000000013") }
    };

    foreach (var stale in staleBindings)
    {
      AssertCode("MAIL_ATTESTATION_BINDING_MISMATCH", () => MailAuthorizationAttestationVerifier.VerifyBound(
        payload, signature, Options(signer, TimeSpan.FromHours(24)), stale, Now));
    }
  }

  [Fact]
  public void MissingSourceDependencyHash_IsRejected()
  {
    var original = CreateAttestation(CreateSnapshot(MailAuthorizationModel.LegacyApplicationAccessPolicy),
      Now, Now.AddHours(1));
    var incompleteSource = original.Source with { PlanSchemaSha256 = null! };

    AssertCode(MailAuthorizationAttestationCanonicalizer.InvalidCode, () =>
      MailAuthorizationAttestationCanonicalizer.Create(original.ConnectionRevisionId, original.ConsentAttemptId,
        original.PlanSha256, incompleteSource, original.ObservedAtUtc, original.ExpiresAtUtc,
        original.ProviderSnapshot));
  }

  [Fact]
  public void MultipleCmsSigners_AreRejected()
  {
    using var signer = CreateSyntheticSigner(Now);
    var payload = MailAuthorizationAttestationCanonicalizer.SerializeCanonical(
      CreateAttestation(CreateSnapshot(MailAuthorizationModel.ExchangeRbacResourceScope), Now, Now.AddHours(1)));

    AssertCode("MAIL_ATTESTATION_SIGNATURE_INVALID", () => MailAuthorizationAttestationVerifier.Verify(
      payload, SignDetached(payload, signer, signerCount: 2), Options(signer, TimeSpan.FromHours(24)), Now));
  }

  [Fact]
  public void SignerCertificateExpiredAtObservation_IsRejected()
  {
    using var signer = CreateSyntheticSigner(Now, Now.AddHours(-1));
    var payload = MailAuthorizationAttestationCanonicalizer.SerializeCanonical(
      CreateAttestation(CreateSnapshot(MailAuthorizationModel.LegacyApplicationAccessPolicy), Now, Now.AddMinutes(30)));

    AssertCode("MAIL_ATTESTATION_SIGNER_INVALID", () => MailAuthorizationAttestationVerifier.Verify(
      payload, SignDetached(payload, signer), Options(signer, TimeSpan.FromHours(24)), Now));
  }

  [Fact]
  public void MailOnlyTokenValidator_AcceptsLegacyExactRoleAndRbacRolelessTokens()
  {
    var now = DateTimeOffset.UtcNow;
    var tenant = Guid.NewGuid();
    var client = Guid.NewGuid();
    var legacy = CreateAccessToken(tenant, client, now, ["Mail.Send"]);
    var rbac = CreateAccessToken(tenant, client, now, roles: null);
    var rbacEmptyRoles = CreateAccessToken(tenant, client, now, []);

    GraphMailAccessTokenValidator.Validate(legacy, MailAuthorizationModel.LegacyApplicationAccessPolicy,
      tenant, client, now);
    GraphMailAccessTokenValidator.Validate(rbac, MailAuthorizationModel.ExchangeRbacResourceScope,
      tenant, client, now);
    GraphMailAccessTokenValidator.Validate(rbacEmptyRoles, MailAuthorizationModel.ExchangeRbacResourceScope,
      tenant, client, now);
  }

  [Theory]
  [InlineData("wrong-tenant")]
  [InlineData("wrong-audience")]
  [InlineData("expired")]
  [InlineData("overlong")]
  [InlineData("missing-client")]
  [InlineData("conflicting-client")]
  [InlineData("missing-legacy-role")]
  [InlineData("extra-legacy-role")]
  [InlineData("role-in-rbac")]
  [InlineData("non-array-rbac-role")]
  [InlineData("delegated-scope")]
  public void MailOnlyTokenValidator_RejectsWrongModelOrUnboundClaims(string mutation)
  {
    var now = DateTimeOffset.UtcNow;
    var tenant = Guid.NewGuid();
    var client = Guid.NewGuid();
    var model = mutation is "role-in-rbac" or "non-array-rbac-role"
      ? MailAuthorizationModel.ExchangeRbacResourceScope
      : MailAuthorizationModel.LegacyApplicationAccessPolicy;
    var claims = new Dictionary<string, object>
    {
      ["tid"] = mutation == "wrong-tenant" ? Guid.NewGuid().ToString("D") : tenant.ToString("D"),
      ["aud"] = mutation == "wrong-audience" ? "api://not-microsoft-graph" : GraphMailAccessTokenValidator.GraphAudience,
      ["appid"] = client.ToString("D"),
      ["exp"] = mutation switch
      {
        "expired" => now.AddSeconds(-1).ToUnixTimeSeconds(),
        "overlong" => now.AddHours(3).ToUnixTimeSeconds(),
        _ => now.AddMinutes(20).ToUnixTimeSeconds()
      }
    };
    if (mutation != "missing-client") claims["appid"] = client.ToString("D");
    else claims.Remove("appid");
    if (mutation == "conflicting-client") claims["azp"] = Guid.NewGuid().ToString("D");
    if (model == MailAuthorizationModel.LegacyApplicationAccessPolicy && mutation != "missing-legacy-role")
      claims["roles"] = mutation == "extra-legacy-role" ? new[] { "Mail.Send", "User.Read" } : new[] { "Mail.Send" };
    else if (mutation == "role-in-rbac") claims["roles"] = new[] { "Mail.Send" };
    else if (mutation == "non-array-rbac-role") claims["roles"] = "Mail.Send";
    if (mutation == "delegated-scope") claims["scp"] = "Mail.Send";

    var token = CreateAccessToken(claims);
    var exception = Assert.Throws<GraphMailAccessTokenValidationException>(() =>
      GraphMailAccessTokenValidator.Validate(token, model, tenant, client, now));
    Assert.Equal("mail-authorization-token-invalid", exception.Code);
  }

  [Fact]
  public async Task EnrolledEvidenceRetainsExactBytesAndCannotOutliveItsCurrentBinding()
  {
    await using var fixture = await TenantAdministrationFixture.CreateAsync();
    await fixture.ConnectAndVerifyAsync();
    await using var db = fixture.Db();
    var consent = await db.TenantConsentAttempts.AsNoTracking().SingleAsync(x => x.State == TenantConsentAttemptStates.ConsentVerified);
    var now = DateTimeOffset.UtcNow;
    var expiryTicks = now.AddHours(1).UtcTicks;
    var expiresAt = new DateTimeOffset(expiryTicks - expiryTicks % 10 + 7, TimeSpan.Zero);
    Assert.Equal(7L, expiresAt.UtcTicks % 10);
    var snapshot = CreateSnapshot(MailAuthorizationModel.LegacyApplicationAccessPolicy, Guid.Parse(fixture.TenantId));
    using var signer = CreateSyntheticSigner(now);
    var attestation = CreateAttestation(snapshot, now.AddMinutes(-1), expiresAt, fixture.ConnectionId, consent.Id);
    var payload = MailAuthorizationAttestationCanonicalizer.SerializeCanonical(attestation);
    var cms = SignDetached(payload, signer);
    var runtime = new MailAuthorizationRuntimeOptions(fixture.FirmId, snapshot.TenantId, snapshot.PermissionModel,
      snapshot.RuntimeClientId, snapshot.EnterpriseServicePrincipalObjectId, snapshot.SenderMailboxObjectIds,
      snapshot.ControlMailboxObjectId, attestation.PlanSha256, attestation.Source, attestation.Source.RepositoryCommit,
      Options(signer, TimeSpan.FromHours(24)).PinnedSignerCertificateSha256, TimeSpan.FromHours(24));
    var options = fixture.Options with { OutboundMailEnabled = true, VerificationMaxAge = TimeSpan.FromHours(24), MailAuthorization = runtime, MailEvidenceEnrollmentEnabled = true };
    var factory = new TenantAdministrationDbFactory(fixture.Pg.Options);
    db.TenantCapabilityVerifications.Add(new TenantCapabilityVerification
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, TenantId = fixture.TenantId,
      Capability = Microsoft365Capabilities.OutboundMail, Permission = "Mail.Send", State = CapabilityVerificationStates.Verified,
      DiagnosticCode = "historical-role-only", ObservedByUserId = fixture.Admin.Id, ObservedAt = now.AddMinutes(-5)
    });
    var staff = TenantAdministrationFixture.User(fixture.FirmId, fixture.TenantId, "no-admin@example.test", "No administrator grant");
    db.Users.Add(staff);
    await db.SaveChangesAsync();
    var oldStatus = (await TenantCapabilityService.StatusesAsync(db, fixture.FirmId, options, DateTimeOffset.UtcNow)).Single(x => x.Capability == Microsoft365Capabilities.OutboundMail);
    Assert.Equal(TenantCapabilityService.NotVerified, oldStatus.State);
    Assert.Null(oldStatus.LastVerifiedAt);
    var denied = await MailAuthorizationEvidenceEnrollment.EnrollAsync(db, factory,
      new ActorContext(staff.Id, fixture.FirmId, staff.SessionEpoch, ["Administrator"]), options, new(payload, cms), TimeProvider.System);
    Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
    var wrongBinding = await MailAuthorizationEvidenceEnrollment.EnrollAsync(db, factory, fixture.AdminActor,
      options with { MailAuthorization = runtime with { PlanSha256 = new string('9', 64) } }, new(payload, cms), TimeProvider.System);
    Assert.Equal(ErrorCodes.GateBlocked, wrongBinding.ErrorCode);
    var expired = CreateAttestation(snapshot, now.AddHours(-2), now.AddHours(-1), fixture.ConnectionId, consent.Id);
    var expiredPayload = MailAuthorizationAttestationCanonicalizer.SerializeCanonical(expired);
    var stale = await MailAuthorizationEvidenceEnrollment.EnrollAsync(db, factory, fixture.AdminActor, options,
      new(expiredPayload, SignDetached(expiredPayload, signer)), TimeProvider.System);
    Assert.Equal(ErrorCodes.GateBlocked, stale.ErrorCode);
    Assert.Equal(1, await db.TenantCapabilityVerifications.CountAsync(x => x.Capability == Microsoft365Capabilities.OutboundMail));

    var enrolled = await MailAuthorizationEvidenceEnrollment.EnrollAsync(db, factory, fixture.AdminActor, options, new(payload, cms), TimeProvider.System);
    Assert.True(enrolled.Succeeded, enrolled.Message);
    var replay = await MailAuthorizationEvidenceEnrollment.EnrollAsync(db, factory, fixture.AdminActor, options, new(payload, cms), TimeProvider.System);
    Assert.Equal(enrolled.Value, replay.Value);
    var retained = await db.TenantCapabilityVerifications.AsNoTracking().SingleAsync(x => x.Id == enrolled.Value);
    Assert.Equal(payload, retained.MailCanonicalPayload);
    Assert.Equal(cms, retained.MailDetachedCms);
    var persistedExpiry = attestation.ExpiresAtUtc.AddTicks(-7);
    Assert.Equal(persistedExpiry, retained.MailEvidenceExpiresAtUtc);
    Assert.Equal(2, await db.TenantCapabilityVerifications.CountAsync(x => x.Capability == Microsoft365Capabilities.OutboundMail));
    var status = (await TenantCapabilityService.StatusesAsync(db, fixture.FirmId, options, DateTimeOffset.UtcNow)).Single(x => x.Capability == Microsoft365Capabilities.OutboundMail);
    Assert.Equal(CapabilityVerificationStates.Verified, status.State);
    Assert.Equal(enrolled.Value, status.AuthorizationEvidence!.ReceiptId);
    Assert.Equal(AttestationDigest(payload, cms), status.AuthorizationEvidence.AttestationSha256);
    Assert.Equal(attestation.ExpiresAtUtc, status.AuthorizationEvidence.ExpiresAtUtc);
    var source = new EnrolledMailAuthorizationAttestationSource(factory, runtime);
    var workerReceipt = await new MailAuthorizationRuntimeGate(factory, source, runtime, TimeProvider.System)
      .RequireCurrentAsync(fixture.FirmId, snapshot.TenantId, snapshot.RuntimeClientId, snapshot.SenderMailboxObjectIds[0], CancellationToken.None);
    Assert.Equal(status.AuthorizationEvidence.AttestationSha256, workerReceipt.AttestationDigest);
    await Assert.ThrowsAsync<IOException>(() => new EnrolledMailAuthorizationAttestationSource(factory,
      runtime with { FirmId = Guid.NewGuid() }).ReadAsync(CancellationToken.None));
    var changed = (await TenantCapabilityService.StatusesAsync(db, fixture.FirmId,
      options with { MailAuthorization = runtime with { PlanSha256 = new string('9', 64) } }, DateTimeOffset.UtcNow)).Single(x => x.Capability == Microsoft365Capabilities.OutboundMail);
    Assert.Equal(TenantCapabilityService.NotVerified, changed.State);
    var pastExpiry = (await TenantCapabilityService.StatusesAsync(db, fixture.FirmId, options, now.AddHours(2))).Single(x => x.Capability == Microsoft365Capabilities.OutboundMail);
    Assert.Equal(TenantCapabilityService.NotVerified, pastExpiry.State);
    var beforePersistedExpiry = (await TenantCapabilityService.StatusesAsync(db, fixture.FirmId, options, persistedExpiry.AddTicks(-1))).Single(x => x.Capability == Microsoft365Capabilities.OutboundMail);
    Assert.Equal(CapabilityVerificationStates.Verified, beforePersistedExpiry.State);
    Assert.Equal(enrolled.Value, beforePersistedExpiry.AuthorizationEvidence!.ReceiptId);
    var atPersistedExpiry = (await TenantCapabilityService.StatusesAsync(db, fixture.FirmId, options, persistedExpiry)).Single(x => x.Capability == Microsoft365Capabilities.OutboundMail);
    Assert.Equal(TenantCapabilityService.NotVerified, atPersistedExpiry.State);
    Assert.Null(atPersistedExpiry.AuthorizationEvidence);
    db.TenantConsentAttempts.Add(new TenantConsentAttempt
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, SetupDraftId = consent.SetupDraftId, InitiatedByUserId = fixture.Admin.Id,
      InitiatingSessionEpoch = fixture.AdminActor.SessionEpoch, InitiatorObjectId = fixture.Admin.Subject,
      ExpectedTenantId = fixture.TenantId, ApplicationClientId = consent.ApplicationClientId, StateHash = new string('3', 64),
      State = TenantConsentAttemptStates.Denied, CreatedAt = now.AddSeconds(1), ExpiresAt = now.AddMinutes(11)
    });
    await db.SaveChangesAsync();
    var revoked = (await TenantCapabilityService.StatusesAsync(db, fixture.FirmId, options, DateTimeOffset.UtcNow)).Single(x => x.Capability == Microsoft365Capabilities.OutboundMail);
    Assert.Equal(TenantCapabilityService.NotVerified, revoked.State);
    Assert.Null(revoked.AuthorizationEvidence);
    await Assert.ThrowsAsync<OperationBlockedException>(() => new MailAuthorizationRuntimeGate(factory, source, runtime, TimeProvider.System)
      .RequireCurrentAsync(fixture.FirmId, snapshot.TenantId, snapshot.RuntimeClientId, snapshot.SenderMailboxObjectIds[0], CancellationToken.None));
    var downgrade = await Record.ExceptionAsync(() => db.GetService<IMigrator>()
      .MigrateAsync("20261023124118_StaffCostRateFirmScopedUser"));
    Assert.Equal("55000", Assert.IsType<PostgresException>(downgrade?.GetBaseException()).SqlState);
    Assert.Equal(payload, (await db.TenantCapabilityVerifications.AsNoTracking().SingleAsync(x => x.Id == enrolled.Value)).MailCanonicalPayload);
  }

  [Fact]
  public async Task RuntimeGate_BindsPinnedReceiptToCurrentConnectionAndConsent()
  {
    await using var fixture = await TenantAdministrationFixture.CreateAsync();
    await fixture.ConnectAndVerifyAsync();
    await using var db = fixture.Db();
    var consent = await db.TenantConsentAttempts.AsNoTracking()
      .SingleAsync(x => x.State == TenantConsentAttemptStates.ConsentVerified);
    var now = DateTimeOffset.UtcNow;
    var snapshot = CreateSnapshot(MailAuthorizationModel.LegacyApplicationAccessPolicy,
      Guid.Parse(fixture.TenantId));
    using var signer = CreateSyntheticSigner(now);
    var attestation = CreateAttestation(snapshot, now.AddMinutes(-1), now.AddHours(1), fixture.ConnectionId, consent.Id);
    var payload = MailAuthorizationAttestationCanonicalizer.SerializeCanonical(attestation);
    var cms = SignDetached(payload, signer);
    var source = new StaticAttestationSource(payload, cms);
    var runtimeOptions = new MailAuthorizationRuntimeOptions(fixture.FirmId, snapshot.TenantId,
      snapshot.PermissionModel, snapshot.RuntimeClientId, snapshot.EnterpriseServicePrincipalObjectId,
      snapshot.SenderMailboxObjectIds, snapshot.ControlMailboxObjectId, attestation.PlanSha256,
      attestation.Source, attestation.Source.RepositoryCommit,
      Options(signer, TimeSpan.FromHours(24)).PinnedSignerCertificateSha256,
      TimeSpan.FromHours(24));
    var gate = new MailAuthorizationRuntimeGate(new TenantAdministrationDbFactory(fixture.Pg.Options), source,
      runtimeOptions, TimeProvider.System);
    var authorizedSender = snapshot.SenderMailboxObjectIds[0];

    var current = await gate.RequireCurrentAsync(fixture.FirmId, snapshot.TenantId,
      snapshot.RuntimeClientId, authorizedSender, CancellationToken.None);
    Assert.Equal(attestation.ConnectionRevisionId, current.ConnectionRevisionId);
    Assert.Equal(attestation.ConsentAttemptId, current.ConsentAttemptId);
    Assert.Equal(attestation.ProviderSnapshotSha256, current.ProviderSnapshotSha256);
    Assert.Equal(AttestationDigest(payload, cms), current.AttestationDigest);

    var initialConnection = await db.Microsoft365ConnectionRevisions.SingleAsync(x => x.Id == fixture.ConnectionId);
    var initialState = initialConnection.State;
    foreach (var blockedState in new[] { Microsoft365RevisionStates.Suspended, Microsoft365RevisionStates.Blocked })
    {
      initialConnection.State = blockedState;
      await db.SaveChangesAsync();
      var blockedStateFailure = await Assert.ThrowsAsync<OperationBlockedException>(() => gate.RequireCurrentAsync(
        fixture.FirmId, snapshot.TenantId, snapshot.RuntimeClientId, authorizedSender, CancellationToken.None));
      Assert.Equal("mail-authorization-evidence-unavailable", blockedStateFailure.Code);
    }
    initialConnection.State = initialState;
    await db.SaveChangesAsync();

    var newerDeniedConsent = new TenantConsentAttempt
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, SetupDraftId = consent.SetupDraftId,
      InitiatedByUserId = fixture.Admin.Id, InitiatingSessionEpoch = fixture.AdminActor.SessionEpoch,
      InitiatorObjectId = fixture.Admin.Subject, ExpectedTenantId = fixture.TenantId,
      ApplicationClientId = consent.ApplicationClientId, StateHash = new string('2', 64),
      State = TenantConsentAttemptStates.Denied, CreatedAt = now.AddSeconds(1),
      ExpiresAt = now.AddMinutes(11)
    };
    db.TenantConsentAttempts.Add(newerDeniedConsent);
    await db.SaveChangesAsync();
    var deniedAttempt = await Assert.ThrowsAsync<OperationBlockedException>(() => gate.RequireCurrentAsync(
      fixture.FirmId, snapshot.TenantId, snapshot.RuntimeClientId, authorizedSender, CancellationToken.None));
    Assert.Equal("mail-authorization-evidence-unavailable", deniedAttempt.Code);

    newerDeniedConsent.State = TenantConsentAttemptStates.ConsentVerified;
    newerDeniedConsent.ConsentVerifiedAt = now.AddSeconds(2);
    newerDeniedConsent.ConsentingTenantId = fixture.TenantId;
    newerDeniedConsent.ConsentingObjectId = fixture.Admin.Subject;
    var currentConnection = await db.Microsoft365ConnectionRevisions.SingleAsync(x => x.Id == fixture.ConnectionId);
    currentConnection.ConsentState = "REQUIRED";
    await db.SaveChangesAsync();
    var unverifiedConnectionConsent = await Assert.ThrowsAsync<OperationBlockedException>(() => gate.RequireCurrentAsync(
      fixture.FirmId, snapshot.TenantId, snapshot.RuntimeClientId, authorizedSender, CancellationToken.None));
    Assert.Equal("mail-authorization-evidence-unavailable", unverifiedConnectionConsent.Code);

    currentConnection.ConsentState = "VERIFIED";

    db.Microsoft365ConnectionRevisions.Add(new Microsoft365ConnectionRevision
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, Revision = 2, TenantId = fixture.TenantId,
      LoginClientIdReference = "slot:login", RuntimeCredentialReference = "slot:reader",
      State = Microsoft365RevisionStates.ConsentRequired, ConsentState = "REQUIRED", CreatedAt = now
    });
    await db.SaveChangesAsync();
    var blocked = await Assert.ThrowsAsync<OperationBlockedException>(() => gate.RequireCurrentAsync(
      fixture.FirmId, snapshot.TenantId, snapshot.RuntimeClientId, authorizedSender, CancellationToken.None));
    Assert.Equal("mail-authorization-evidence-unavailable", blocked.Code);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task RuntimeGate_BlocksConnectionAndLatestConsentDriftDuringEvidenceReadBeforeMailDispatch(bool replaceTenant)
  {
    await using var fixture = await TenantAdministrationFixture.CreateAsync();
    await fixture.ConnectAndVerifyAsync();
    TenantConsentAttempt consent;
    await using (var db = fixture.Db())
      consent = await db.TenantConsentAttempts.AsNoTracking()
        .SingleAsync(x => x.State == TenantConsentAttemptStates.ConsentVerified);

    var now = DateTimeOffset.UtcNow;
    var snapshot = CreateSnapshot(MailAuthorizationModel.LegacyApplicationAccessPolicy,
      Guid.Parse(fixture.TenantId));
    using var signer = CreateSyntheticSigner(now);
    var attestation = CreateAttestation(snapshot, now.AddMinutes(-1), now.AddHours(1), fixture.ConnectionId, consent.Id);
    var payload = MailAuthorizationAttestationCanonicalizer.SerializeCanonical(attestation);
    var cms = SignDetached(payload, signer);
    var source = new CallbackAttestationSource(async ct =>
    {
      await using var mutationDb = fixture.Db();
      if (replaceTenant)
      {
        // The older verified connection and consent deliberately remain valid. Filtering by
        // tenant before choosing the latest revision would wrongly fall back to them.
        mutationDb.Microsoft365ConnectionRevisions.Add(new Microsoft365ConnectionRevision
        {
          Id = Guid.NewGuid(), FirmId = fixture.FirmId, Revision = 2,
          TenantId = Guid.NewGuid().ToString("D"), LoginClientIdReference = "slot:login",
          RuntimeCredentialReference = "slot:reader", State = Microsoft365RevisionStates.ConsentRequired,
          ConsentState = "VERIFIED", CreatedAt = now.AddSeconds(1)
        });
      }
      else
      {
        var connection = await mutationDb.Microsoft365ConnectionRevisions
          .SingleAsync(x => x.Id == fixture.ConnectionId, ct);
        connection.VerifiedAt = connection.VerifiedAt?.AddSeconds(1) ?? now.AddSeconds(-1);
        mutationDb.TenantConsentAttempts.Add(new TenantConsentAttempt
        {
          Id = Guid.NewGuid(), FirmId = fixture.FirmId, SetupDraftId = consent.SetupDraftId,
          InitiatedByUserId = fixture.Admin.Id, InitiatingSessionEpoch = fixture.AdminActor.SessionEpoch,
          InitiatorObjectId = fixture.Admin.Subject, ExpectedTenantId = fixture.TenantId,
          ApplicationClientId = consent.ApplicationClientId,
          StateHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(),
          State = TenantConsentAttemptStates.ConsentVerified, CreatedAt = now.AddSeconds(1),
          ExpiresAt = now.AddMinutes(11), ConsentVerifiedAt = now.AddSeconds(2),
          ConsentingTenantId = fixture.TenantId, ConsentingObjectId = fixture.Admin.Subject
        });
      }
      await mutationDb.SaveChangesAsync(ct);
      return new MailAuthorizationAttestationBundle(payload, cms);
    });
    var runtimeOptions = new MailAuthorizationRuntimeOptions(fixture.FirmId, snapshot.TenantId,
      snapshot.PermissionModel, snapshot.RuntimeClientId, snapshot.EnterpriseServicePrincipalObjectId,
      snapshot.SenderMailboxObjectIds, snapshot.ControlMailboxObjectId, attestation.PlanSha256,
      attestation.Source, attestation.Source.RepositoryCommit,
      Options(signer, TimeSpan.FromHours(24)).PinnedSignerCertificateSha256,
      TimeSpan.FromHours(24));
    var gate = new MailAuthorizationRuntimeGate(new TenantAdministrationDbFactory(fixture.Pg.Options), source,
      runtimeOptions, TimeProvider.System);

    var certificateDirectory = Path.Combine(Path.GetTempPath(), "auditsphere-mail-runtime-gate-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(certificateDirectory);
    try
    {
      var certificatePath = Path.Combine(certificateDirectory, "certificate.pem");
      var privateKeyPath = Path.Combine(certificateDirectory, "private-key.pem");
      await File.WriteAllTextAsync(certificatePath, signer.ExportCertificatePem());
      using (var privateKey = signer.GetRSAPrivateKey()!)
        await File.WriteAllTextAsync(privateKeyPath, privateKey.ExportPkcs8PrivateKeyPem());

      var graphOptions = new GraphMailOptions(snapshot.TenantId.ToString("D"), snapshot.RuntimeClientId.ToString("D"),
        snapshot.SenderMailboxObjectIds[0].ToString("D"), certificatePath, privateKeyPath, fixture.FirmId);
      var handler = new MailAuthorizationRequestCountingHandler(snapshot.TenantId, snapshot.RuntimeClientId);
      using var http = new HttpClient(handler);
      var sender = new GraphPbcMailSender(http, graphOptions, gate, TimeProvider.System);

      var failure = await Record.ExceptionAsync(() => sender.SendAsync(
        new PbcMailPlan("recipient@example.test", "Subject", "Body", Guid.NewGuid(), FirmId: fixture.FirmId),
        CancellationToken.None));

      Assert.Equal(1, source.Calls);
      Assert.Equal(0, handler.TokenCalls);
      Assert.Equal(0, handler.GraphCalls);
      var blocked = Assert.IsType<OperationBlockedException>(failure);
      Assert.Equal("mail-authorization-evidence-unavailable", blocked.Code);
    }
    finally
    {
      Directory.Delete(certificateDirectory, recursive: true);
    }
  }

  private static MailAuthorizationAttestation CreateAttestation(MailAuthorizationProviderSnapshot snapshot,
    DateTimeOffset observed, DateTimeOffset expires, Guid? connectionRevisionId = null, Guid? consentAttemptId = null) =>
    MailAuthorizationAttestationCanonicalizer.Create(
      connectionRevisionId ?? Guid.Parse("10000000-0000-4000-8000-000000000001"),
      consentAttemptId ?? Guid.Parse("10000000-0000-4000-8000-000000000002"),
      new string('a', 64),
      new MailAuthorizationSourceIdentity(
        "0123456789abcdef0123456789abcdef01234567", new string('b', 64), new string('c', 64), new string('d', 64),
        new string('e', 64), new string('f', 64), new string('1', 64)),
      observed, expires, snapshot);

  private static MailAuthorizationAttestationExpectedBinding ExpectedBinding(MailAuthorizationAttestation attestation)
  {
    var snapshot = attestation.ProviderSnapshot;
    return new MailAuthorizationAttestationExpectedBinding(snapshot.TenantId, snapshot.PermissionModel,
      snapshot.RuntimeClientId, snapshot.EnterpriseServicePrincipalObjectId, attestation.ConnectionRevisionId,
      attestation.ConsentAttemptId, attestation.PlanSha256, attestation.Source,
      snapshot.SenderMailboxObjectIds, snapshot.ControlMailboxObjectId);
  }

  private static MailAuthorizationProviderSnapshot CreateSnapshot(MailAuthorizationModel model, Guid? expectedTenantId = null)
  {
    var tenant = expectedTenantId ?? Guid.Parse("20000000-0000-4000-8000-000000000001");
    var client = Guid.Parse("20000000-0000-4000-8000-000000000002");
    var enterpriseSp = Guid.Parse("20000000-0000-4000-8000-000000000003");
    var graphSp = Guid.Parse("20000000-0000-4000-8000-000000000004");
    var mailSendRole = Guid.Parse("20000000-0000-4000-8000-000000000005");
    var senderA = Guid.Parse("20000000-0000-4000-8000-000000000010");
    var senderB = Guid.Parse("20000000-0000-4000-8000-000000000011");
    var control = Guid.Parse("20000000-0000-4000-8000-000000000012");
    var senders = new[] { senderB, senderA };
    var grants = model == MailAuthorizationModel.LegacyApplicationAccessPolicy
      ? new[] { new MailEntraAppRoleAssignment(Guid.Parse("20000000-0000-4000-8000-000000000020"),
          enterpriseSp, graphSp, mailSendRole) }
      : Array.Empty<MailEntraAppRoleAssignment>();
    var exchangeAssignments = model == MailAuthorizationModel.ExchangeRbacResourceScope
      ? new[] { new MailExchangeRoleAssignment("Application Mail.Send", enterpriseSp, "ServicePrincipal", "Direct") }
      : Array.Empty<MailExchangeRoleAssignment>();

    var legacy = model == MailAuthorizationModel.LegacyApplicationAccessPolicy
      ? new MailLegacyAuthorizationEvidence(
          Guid.Parse("20000000-0000-4000-8000-000000000030"), "Synthetic-Sender-Group", "Synthetic-Sender-Group",
          "RestrictAccess", senders, [client],
          [new MailLegacyAccessProbe(senderA, true), new MailLegacyAccessProbe(senderB, true), new MailLegacyAccessProbe(control, false)])
      : null;

    var rbac = model == MailAuthorizationModel.ExchangeRbacResourceScope
      ? new MailExchangeRbacEvidence(enterpriseSp, client, "Synthetic-Sender-Scope", "RecipientScope", false,
          "Synthetic test filter bound to this in-memory sender set.", senders,
          [new MailExchangeAuthorizationProbe(senderA, "Application Mail.Send", ["Mail.Send"], "Synthetic-Sender-Scope", "CustomRecipientScope", true),
           new MailExchangeAuthorizationProbe(senderB, "Application Mail.Send", ["Mail.Send"], "Synthetic-Sender-Scope", "CustomRecipientScope", true),
           new MailExchangeAuthorizationProbe(control, "Application Mail.Send", ["Mail.Send"], "Synthetic-Sender-Scope", "CustomRecipientScope", false)])
      : null;

    return new MailAuthorizationProviderSnapshot(tenant, model, client, enterpriseSp, graphSp,
      Guid.Parse("00000003-0000-0000-c000-000000000000"), mailSendRole, "Mail.Send", true, ["Application"],
      senders, control, grants, exchangeAssignments, legacy, rbac);
  }

  private static X509Certificate2 CreateSyntheticSigner(DateTimeOffset now, DateTimeOffset? notAfter = null)
  {
    using var rsa = RSA.Create(2048);
    var request = new CertificateRequest("CN=AuditSphere Synthetic Mail Attestation", rsa, HashAlgorithmName.SHA256,
      RSASignaturePadding.Pkcs1);
    request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
    return request.CreateSelfSigned(now.AddDays(-1), notAfter ?? now.AddDays(30));
  }

  private static byte[] SignDetached(byte[] payload, X509Certificate2 signer, int signerCount = 1,
    X509Certificate2? unrelatedCertificate = null)
  {
    var cms = new SignedCms(new ContentInfo(payload), detached: true);
    for (var i = 0; i < signerCount; i++)
    {
      var cmsSigner = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, signer)
      {
        DigestAlgorithm = new Oid(Sha256Oid),
        IncludeOption = X509IncludeOption.EndCertOnly
      };
      cms.ComputeSignature(cmsSigner, silent: true);
    }
    if (unrelatedCertificate is not null) cms.AddCertificate(unrelatedCertificate);
    return cms.Encode();
  }

  private static byte[] SignAttached(byte[] payload, X509Certificate2 signer)
  {
    var cms = new SignedCms(new ContentInfo(payload), detached: false);
    cms.ComputeSignature(new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, signer)
    {
      DigestAlgorithm = new Oid(Sha256Oid),
      IncludeOption = X509IncludeOption.EndCertOnly
    }, silent: true);
    return cms.Encode();
  }

  private static MailAuthorizationAttestationVerifierOptions Options(X509Certificate2 signer, TimeSpan maximumAge) =>
    new(Convert.ToHexString(signer.GetCertHash(HashAlgorithmName.SHA256)).ToLowerInvariant(), maximumAge);

  private static string CreateAccessToken(Guid tenantId, Guid clientId, DateTimeOffset now,
    string[]? roles) => CreateAccessToken(new Dictionary<string, object>
  {
    ["tid"] = tenantId.ToString("D"),
    ["aud"] = GraphMailAccessTokenValidator.GraphAudience,
    ["appid"] = clientId.ToString("D"),
    ["exp"] = now.AddMinutes(15).ToUnixTimeSeconds(),
    ["roles"] = roles is null ? Array.Empty<string>() : roles
  }, omitRoles: roles is null);

  private static string CreateAccessToken(IReadOnlyDictionary<string, object> claims, bool omitRoles = false)
  {
    var payload = omitRoles ? claims.Where(x => x.Key != "roles").ToDictionary(x => x.Key, x => x.Value) : claims;
    var header = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\"}"))
      .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    var body = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(payload))
      .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    return $"{header}.{body}.AQID";
  }

  private static string AttestationDigest(byte[] payload, byte[] cms)
  {
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    Span<byte> length = stackalloc byte[sizeof(int)];
    BinaryPrimitives.WriteInt32BigEndian(length, payload.Length);
    hash.AppendData(length);
    hash.AppendData(payload);
    BinaryPrimitives.WriteInt32BigEndian(length, cms.Length);
    hash.AppendData(length);
    hash.AppendData(cms);
    return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
  }

  private sealed class StaticAttestationSource(byte[] payload, byte[] cms) : IMailAuthorizationAttestationSource
  {
    public Task<MailAuthorizationAttestationBundle> ReadAsync(CancellationToken ct) =>
      Task.FromResult(new MailAuthorizationAttestationBundle(payload, cms));
  }

  private sealed class CallbackAttestationSource(
    Func<CancellationToken, Task<MailAuthorizationAttestationBundle>> read) : IMailAuthorizationAttestationSource
  {
    public int Calls { get; private set; }

    public Task<MailAuthorizationAttestationBundle> ReadAsync(CancellationToken ct)
    {
      Calls++;
      return read(ct);
    }
  }

  private sealed class MailAuthorizationRequestCountingHandler(Guid tenantId, Guid clientId) : HttpMessageHandler
  {
    public int TokenCalls { get; private set; }
    public int GraphCalls { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
      if (request.RequestUri?.Host == "login.microsoftonline.com")
      {
        TokenCalls++;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = JsonContent.Create(new
          {
            access_token = CreateAccessToken(tenantId, clientId, DateTimeOffset.UtcNow, ["Mail.Send"])
          })
        });
      }

      GraphCalls++;
      return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted));
    }
  }

  private sealed class TenantAdministrationDbFactory(DbContextOptions<AuditSphereDbContext> options)
    : IAuditSphereDbContextFactory
  {
    public Task<IAuditSphereDbContext> CreateAsync(CancellationToken ct = default) =>
      Task.FromResult<IAuditSphereDbContext>(new AuditSphereDbContext(options));
  }

  private static void AssertCode(string expected, Action action)
  {
    var exception = Assert.Throws<MailAuthorizationAttestationException>(action);
    Assert.Equal(expected, exception.Code);
  }
}
