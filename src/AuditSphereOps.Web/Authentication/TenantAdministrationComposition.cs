using System.Security.Claims;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Web.Authentication;

/// <summary>Deployment-owned tenant-administration settings. Secrets are file paths to approved mounts only.</summary>
public sealed record TenantAdministrationSettings(
  TenantAdministrationOptions Options,
  bool Simulation,
  bool ConsentEnabled,
  string? MailSender)
{
  public static TenantAdministrationSettings From(IConfiguration configuration)
  {
    var section = configuration.GetSection("TenantAdministration");
    var tenant = configuration["Identity:TenantId"] ?? configuration["DevelopmentIdentity:TenantId"] ?? string.Empty;
    var simulation = section.GetValue<bool>("Simulation:Enabled");
    bool Enabled(string name) => section.GetValue<bool>($"{name}:Enabled");
    return new(new TenantAdministrationOptions(
        tenant,
        ProvisioningEnabled: Enabled("Provisioning"),
        GuestInvitationEnabled: Enabled("GuestInvitation"),
        GroupMembershipEnabled: Enabled("GroupMembership"),
        DirectoryReadEnabled: configuration.GetValue<bool>("DirectoryReader:Enabled") || simulation,
        OutboundMailEnabled: Enabled("OutboundMail"),
        GuestRedirectUrl: section["GuestRedirectUrl"],
        VerificationMaxAge: TimeSpan.FromHours(Math.Clamp(section.GetValue<int?>("VerificationMaxAgeHours") ?? 24, 1, 168))),
      simulation,
      simulation || configuration.GetValue<bool>("TenantConsent:Enabled") &&
        !string.Equals(configuration["TenantConsent:ClientId"], configuration["Identity:ClientId"], StringComparison.OrdinalIgnoreCase),
      section["OutboundMail:SenderMailbox"]);
  }
}

public static class TenantAdministrationComposition
{
  /// <summary>The only roles the shared tenant-administration identity may hold.</summary>
  public static readonly IReadOnlySet<string> TenantAdministrationRoleSet =
    new HashSet<string>(StringComparer.Ordinal) { "User.Create", "User.Invite.All", "GroupMember.ReadWrite.All" };

  public static void Register(WebApplicationBuilder builder)
  {
    var configuration = builder.Configuration;
    var settings = TenantAdministrationSettings.From(configuration);
    var section = configuration.GetSection("TenantAdministration");
    if (settings.Simulation)
    {
      // Fail-closed startup guard: simulation is never available to a real deployment.
      if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Test"))
        throw new InvalidOperationException("Tenant administration simulation is allowed only in Development or Test.");
      if (configuration.GetValue<bool>("ExternalEffects:Enabled") || !configuration.GetValue<bool>("Application:AllowSimulationAdapters"))
        throw new InvalidOperationException("Tenant administration simulation requires AllowSimulationAdapters and disabled external effects.");
      if (configuration.GetValue<bool>("DirectoryReader:Enabled") || configuration.GetValue<bool>("TenantConsent:Enabled"))
        throw new InvalidOperationException("Tenant administration simulation cannot be combined with live Microsoft credentials.");
      if (!Guid.TryParse(settings.Options.TenantId, out _))
        throw new InvalidOperationException("Tenant administration simulation requires a GUID tenant ID.");
      var users = section.GetSection("Simulation:Users").Get<List<SimulatedDirectoryUser>>() ?? [];
      var groups = section.GetSection("Simulation:Groups").Get<List<SimulatedDirectoryGroup>>() ?? [];
      var notGranted = section.GetSection("Simulation:NotGranted").Get<List<string>>() ?? [];
      var simulator = new SimulatedMicrosoftTenant(settings.Options.TenantId, users, groups, notGranted);
      builder.Services.AddSingleton(simulator);
      builder.Services.AddSingleton<IMicrosoftDirectoryReader>(simulator);
      builder.Services.AddSingleton<IMicrosoftDirectoryUserProvisioner>(simulator);
      builder.Services.AddSingleton<IMicrosoftGuestInvitationProvider>(simulator);
      builder.Services.AddSingleton<IMicrosoftGroupMembershipProvider>(simulator);
      builder.Services.AddSingleton<IMicrosoftTenantConsentVerifier>(simulator);
    }
    else
    {
      // Each capability uses its own app identity holding exactly its documented permission. The three
      // tenant-administration capabilities may share one approved identity limited to exactly their roles.
      GraphCapabilityCredentialOptions Credential(string path, string role, bool enabled,
        IReadOnlySet<string>? approved = null) => new(enabled,
        settings.Options.TenantId, configuration[$"{path}:ClientId"] ?? string.Empty,
        configuration[$"{path}:CertificatePath"] ?? string.Empty, configuration[$"{path}:PrivateKeyPath"] ?? string.Empty, role, approved);
      var administrationRoles = TenantAdministrationRoleSet;
      var reader = Credential("DirectoryReader", "User.Read.All", configuration.GetValue<bool>("DirectoryReader:Enabled"));
      var provisioning = Credential("TenantAdministration:Provisioning", "User.Create", settings.Options.ProvisioningEnabled, administrationRoles);
      var invitation = Credential("TenantAdministration:GuestInvitation", "User.Invite.All", settings.Options.GuestInvitationEnabled, administrationRoles);
      var groupMembership = Credential("TenantAdministration:GroupMembership", "GroupMember.ReadWrite.All", settings.Options.GroupMembershipEnabled, administrationRoles);
      var mail = Credential("TenantAdministration:OutboundMail", "Mail.Send", settings.Options.OutboundMailEnabled);
      var selectedSite = Credential("SelectedSite", "Sites.Selected", !string.IsNullOrWhiteSpace(configuration["SelectedSite:ClientId"]));
      foreach (var (name, credential) in new[] { ("Provisioning", provisioning), ("GuestInvitation", invitation),
                 ("GroupMembership", groupMembership), ("OutboundMail", mail) })
      {
        if (!credential.Enabled) continue;
        if (!credential.IsComplete)
          throw new InvalidOperationException($"TenantAdministration:{name} is enabled without a complete separate certificate credential.");
        if (string.Equals(credential.ClientId, configuration["Identity:ClientId"], StringComparison.OrdinalIgnoreCase) ||
            string.Equals(credential.ClientId, configuration["SelectedSite:ClientId"], StringComparison.OrdinalIgnoreCase))
          throw new InvalidOperationException($"TenantAdministration:{name} must use an app identity separate from sign-in and the selected-site worker.");
        if (string.Equals(credential.ClientId, configuration["DirectoryReader:ClientId"], StringComparison.OrdinalIgnoreCase))
          throw new InvalidOperationException($"TenantAdministration:{name} must use an app identity separate from the directory reader.");
      }
      if (mail.Enabled && new[] { provisioning, invitation, groupMembership }.Any(x => x.Enabled &&
            string.Equals(x.ClientId, mail.ClientId, StringComparison.OrdinalIgnoreCase)))
        throw new InvalidOperationException("TenantAdministration:OutboundMail must use its own app identity.");
      if (settings.Options.GuestInvitationEnabled &&
          (!Uri.TryCreate(settings.Options.GuestRedirectUrl, UriKind.Absolute, out var redirect) ||
           redirect.Scheme != Uri.UriSchemeHttps && !(builder.Environment.IsDevelopment() && redirect.IsLoopback)))
        throw new InvalidOperationException("TenantAdministration:GuestRedirectUrl must be an HTTPS application URL (loopback HTTP only in Development).");

      GraphCapabilityTokenSource Tokens(IServiceProvider services, GraphCapabilityCredentialOptions options) =>
        new(services.GetRequiredService<IHttpClientFactory>().CreateClient("tenant-admin-token"), options);
      builder.Services.AddHttpClient("tenant-admin-token").ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
      builder.Services.AddHttpClient("tenant-admin-graph", client => client.Timeout = TimeSpan.FromSeconds(30))
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
      HttpClient Graph(IServiceProvider services) => services.GetRequiredService<IHttpClientFactory>().CreateClient("tenant-admin-graph");
      builder.Services.AddTransient<IMicrosoftDirectoryUserProvisioner>(s => new GraphDirectoryUserProvisioner(Graph(s), Tokens(s, provisioning), Tokens(s, reader)));
      builder.Services.AddTransient<IMicrosoftGuestInvitationProvider>(s => new GraphGuestInvitationProvider(Graph(s), Tokens(s, invitation), Tokens(s, reader)));
      builder.Services.AddTransient<IMicrosoftGroupMembershipProvider>(s => new GraphGroupMembershipProvider(Graph(s), Tokens(s, groupMembership)));
      builder.Services.AddTransient<IMicrosoftTenantConsentVerifier>(s => new GraphTenantConsentVerifier(Graph(s),
        new TenantConsentVerifierOptions(configuration.GetValue<bool>("TenantConsent:Enabled"), settings.Options.TenantId,
          configuration["TenantConsent:ClientId"] ?? string.Empty, configuration["TenantConsent:IdentityRedirectUri"] ?? string.Empty,
          configuration["DirectoryReader:CertificatePath"] ?? string.Empty, configuration["DirectoryReader:PrivateKeyPath"] ?? string.Empty),
        new Dictionary<string, GraphCapabilityTokenSource>
        {
          [Microsoft365Capabilities.DirectoryRead] = Tokens(s, reader),
          [Microsoft365Capabilities.TenantUserProvisioning] = Tokens(s, provisioning),
          [Microsoft365Capabilities.GuestInvitation] = Tokens(s, invitation),
          [Microsoft365Capabilities.GroupMembership] = Tokens(s, groupMembership),
          [Microsoft365Capabilities.OutboundMail] = Tokens(s, mail),
          [Microsoft365Capabilities.SelectedSite] = Tokens(s, selectedSite),
        }, s.GetRequiredService<ILogger<GraphTenantConsentVerifier>>()));
    }
    builder.Services.AddSingleton(settings);
  }

  /// <summary>Consent endpoints. Callback URLs are single-use and carry no reusable secret.</summary>
  public static void MapEndpoints(WebApplication app)
  {
    var settings = app.Services.GetRequiredService<TenantAdministrationSettings>();
    const string page = "/app/administration/microsoft365/tenant-connection";

    app.MapGet("/auth/m365-consent/identity-callback", async (HttpContext http, TrustedActorResolver actorResolver,
      IDbContextFactory<AuditSphereDbContext> dbFactory, IMicrosoftTenantConsentVerifier verifier, CancellationToken ct) =>
    {
      NoStore(http);
      if (!settings.ConsentEnabled) return Results.NotFound();
      var query = http.Request.Query;
      if (query["state"].Count != 1 || query["code"].Count > 1 || query["error"].Count > 1)
        return Results.Redirect(page + "?result=blocked");
      var actor = await actorResolver.ResolveAsync(http.User, ct);
      if (actor is null) return Results.Forbid();
      await using var db = await dbFactory.CreateDbContextAsync(ct);
      var now = DateTimeOffset.UtcNow;
      var result = await TenantConsentService.CompleteIdentityVerificationAsync(db, actor, verifier,
        query["state"].ToString(), query["code"].ToString(), !string.IsNullOrWhiteSpace(query["error"].ToString()), now, ct);
      if (!result.Succeeded) return Results.Redirect(page + "?result=identity-blocked");
      await TenantCapabilityService.VerifyAsync(db, actor, verifier, settings.Options, now, ct);
      return Results.Redirect(page + "?result=verified");
    });

    if (!settings.Simulation) return;

    // Simulated Microsoft pages: they only redirect back with the values Microsoft would return.
    app.MapGet("/auth/m365-consent/simulated-consent", (HttpContext http, string? state) =>
    {
      NoStore(http);
      return string.IsNullOrWhiteSpace(state) || state.Length > 256
        ? Results.BadRequest()
        : Results.Redirect($"/auth/m365-consent/callback?tenant={Uri.EscapeDataString(settings.Options.TenantId)}" +
            $"&state={Uri.EscapeDataString(state)}&admin_consent=True");
    });
    app.MapGet("/auth/m365-consent/simulated-identity", (HttpContext http, SimulatedMicrosoftTenant simulator,
      string? state, string? nonce) =>
    {
      NoStore(http);
      var tenant = http.User.FindFirstValue("tid");
      var objectId = http.User.FindFirstValue("oid");
      if (string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(nonce) ||
          string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(objectId))
        return Results.BadRequest();
      var code = simulator.IssueCode(tenant, objectId, nonce);
      return Results.Redirect($"/auth/m365-consent/identity-callback?state={Uri.EscapeDataString(state)}&code={code}");
    });
  }

  private static void NoStore(HttpContext http)
  {
    http.Response.Headers.CacheControl = "no-store";
    http.Response.Headers["Referrer-Policy"] = "no-referrer";
  }
}
