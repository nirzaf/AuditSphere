using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Api.Authentication;
using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record UiTenantAdministrationResponse(TenantAdministrationWorkspace Workspace, bool ConsentConfigured,
    bool DirectoryConfigured, Guid? ConfiguredTenantId, bool PreparationConfigured, bool Simulation);

  public sealed record UiConsentBegin(Guid DraftId, bool Reviewed);
  /// <summary>Authorization redirect returned by the tenant-consent command.</summary>
  public sealed record UiConsentRedirectResponse(string Redirect);
  public sealed record UiDirectorySearch(string Prefix, string? Domain, string? PageToken, bool Browse);
  public sealed record UiDirectoryBind(Guid ObjectId, bool Guest, bool Reviewed);
  public sealed record UiTenantPreparation(Guid DraftId, string ExpectedRevision, bool Reviewed);

  private static bool ConsentReady(HttpContext http, TenantAdministrationSettings settings) => settings.ConsentEnabled &&
    (settings.Simulation || ConsentRedirect(http, "configuration-check") is not null);
  private static Uri? ConsentRedirect(HttpContext http, string state)
  {
    var config = http.RequestServices.GetRequiredService<IConfiguration>();
    return TenantConsentRedirect.Build(config["Identity:TenantId"], config["TenantConsent:ClientId"],
      config["TenantConsent:RedirectUri"], state, http.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment());
  }

  private static void MapMicrosoft365Endpoints(RouteGroupBuilder group)
  {
    MapTenantOperationEndpoints(group);
    MapTenantSetupEndpoints(group);
    MapSelectedResourceEndpoints(group);
    MapWorkspaceProvisioningEndpoints(group);
    group.MapUiGet("/administration/microsoft365", http => ReadAsync(http, async (db, actor, ct) =>
    {
      var settings = http.RequestServices.GetRequiredService<TenantAdministrationSettings>();
      var config = http.RequestServices.GetRequiredService<IConfiguration>();
      var workspace = await TenantAdministrationWorkspaceQuery.GetAsync(db, actor, settings.Options, DateTimeOffset.UtcNow, ct);
      return workspace.Succeeded ? CommandResult<object>.Ok(new { workspace = workspace.Value,
        consentConfigured = ConsentReady(http, settings), directoryConfigured = settings.Options.DirectoryReadEnabled,
        configuredTenantId = Guid.TryParse(settings.Options.TenantId, out var tenantId) ? tenantId.ToString("D") : null,
        preparationConfigured = InitialAdministratorSignIn.AllowsUnmappedIdentity(config, http.User.FindFirstValue("tid"), http.User.FindFirstValue("oid")),
        simulation = settings.Simulation }) : CommandResult<object>.Fail(workspace.ErrorCode!, workspace.Message!);
    })).WithUiResponse<UiTenantAdministrationResponse>();
    group.MapPost("/administration/microsoft365/prepare", (UiTenantPreparation i, HttpContext http) => CommandAsync(http, async (db, actor, ct) =>
    {
      if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<Guid>();
      if (!long.TryParse(i.ExpectedRevision, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var revision) || revision < 1)
        return CommandResult<Guid>.Fail("request.invalid", "A reviewed setup revision is required.");
      var config = http.RequestServices.GetRequiredService<IConfiguration>();
      var settings = http.RequestServices.GetRequiredService<TenantAdministrationSettings>();
      return await Microsoft365InstallationService.PrepareTenantAsync(db, actor, ApiHost.InstallationOptions(config),
        new(i.DraftId, revision, i.Reviewed), settings.Options.TenantId, "configuration:Identity:ClientId",
        config["SelectedSite:CredentialReference"] ?? "configuration:SelectedSite", DateTimeOffset.UtcNow, ct);
    })).WithUiResponse<UiValueResponse<Guid>>(includeConflict: true);
    group.MapPost("/administration/microsoft365/connect", (UiConsentBegin i, HttpContext http) => CommandAsync(http,
      async (db, actor, ct) =>
      {
        var settings = http.RequestServices.GetRequiredService<TenantAdministrationSettings>();
        if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<object>();
        if (!i.Reviewed || !ConsentReady(http, settings))
          return CommandResult<object>.Fail(ErrorCodes.GateBlocked, "Review the requested permissions and configure the deployment-owned consent callback first.");
        var config = http.RequestServices.GetRequiredService<IConfiguration>();
        var begin = await TenantConsentService.BeginAsync(db, actor, i.DraftId, settings.Options.TenantId,
          settings.Simulation ? "00000000-0000-0000-0000-00000000c0de" : config["TenantConsent:ClientId"] ?? string.Empty,
          DateTimeOffset.UtcNow, ct);
        if (!begin.Succeeded) return CommandResult<object>.Fail(begin.ErrorCode!, begin.Message!);
        var redirect = settings.Simulation ? "/auth/m365-consent/simulated-consent?state=" + Uri.EscapeDataString(begin.Value!.State) :
          ConsentRedirect(http, begin.Value!.State)!.AbsoluteUri;
        return CommandResult<object>.Ok(new { redirect });
      })).WithUiResponse<UiValueResponse<UiConsentRedirectResponse>>();
    group.MapUiPost("/administration/microsoft365/verify", http => CommandAsync(http, async (db, actor, ct) =>
    {
      var settings = http.RequestServices.GetRequiredService<TenantAdministrationSettings>();
      if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<IReadOnlyList<CapabilityStatus>>();
      var config = http.RequestServices.GetRequiredService<IConfiguration>();
      if (settings.Options.DirectoryReadEnabled)
        await DirectoryCapabilityVerificationService.VerifyAsync(db, actor,
          http.RequestServices.GetRequiredService<IMicrosoftDirectoryReader>(), settings.Options.TenantId,
          settings.Simulation ? "00000000-0000-0000-0000-00000000c0de" : config["DirectoryReader:ClientId"] ?? string.Empty, DateTimeOffset.UtcNow, ct);
      return await TenantCapabilityService.VerifyAsync(db, actor, http.RequestServices.GetRequiredService<IMicrosoftTenantConsentVerifier>(),
        settings.Options, DateTimeOffset.UtcNow, ct);
    })).WithUiResponse<UiValueResponse<IReadOnlyList<CapabilityStatus>>>();
    group.MapPost("/administration/directory/search", (UiDirectorySearch i, HttpContext http) => CommandAsync(http, async (db, actor, ct) =>
    {
      var settings = http.RequestServices.GetRequiredService<TenantAdministrationSettings>();
      if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<DirectoryCandidatePage>();
      if (!settings.Options.DirectoryReadEnabled) return CommandResult<DirectoryCandidatePage>.Fail(ErrorCodes.GateBlocked, "The separately configured directory reader is disabled.");
      if (!DirectoryContinuation.TryContext(actor, settings.Options.TenantId, i.Browse, i.Prefix, i.Domain, out var context))
        return CommandResult<DirectoryCandidatePage>.Fail(ErrorCodes.GateBlocked, "Review a valid directory query before continuing.");
      var continuation = new DirectoryContinuation(http.RequestServices.GetRequiredService<IDataProtectionProvider>());
      if (!continuation.TryRead(i.PageToken, context!, DateTimeOffset.UtcNow, out var providerToken))
        return CommandResult<DirectoryCandidatePage>.Fail(ErrorCodes.StaleRevision, "This directory continuation expired or belongs to a different query or session. Start again.");
      var reader = http.RequestServices.GetRequiredService<IMicrosoftDirectoryReader>();
      var result = i.Browse
        ? await DirectoryDiscoveryService.BrowseActiveAsync(db, actor, reader, settings.Options.TenantId, context!.Domain, providerToken, ct)
        : await DirectoryDiscoveryService.SearchAsync(db, actor, reader, settings.Options.TenantId, context!.Prefix!, providerToken, ct, context.Domain);
      if (!result.Succeeded) return result;
      if (!continuation.TryWrite(result.Value!.NextPageToken, context!, DateTimeOffset.UtcNow, out var nextToken))
        return CommandResult<DirectoryCandidatePage>.Fail(ErrorCodes.GateBlocked, "Directory continuation could not be verified. Start again.");
      return CommandResult<DirectoryCandidatePage>.Ok(new(result.Value.Users, nextToken));
    })).WithUiResponse<UiValueResponse<DirectoryCandidatePage>>(includeConflict: true);
    group.MapGet("/administration/directory/binding", (Guid tenantId, Guid objectId, HttpContext http) => ReadAsync(http, async (db, actor, ct) =>
    {
      var settings = http.RequestServices.GetRequiredService<TenantAdministrationSettings>();
      if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct))
        return TenantAdministration.Denied<DirectoryBindingReconciliation>();
      if (!settings.Options.DirectoryReadEnabled)
        return CommandResult<DirectoryBindingReconciliation>.Fail(ErrorCodes.GateBlocked, "The separately configured directory reader is disabled.");
      return await DirectoryUserBindingService.ReconcileAsync(db, actor, settings.Options.TenantId, tenantId, objectId, ct);
    })).WithUiResponse<DirectoryBindingReconciliation>();
    group.MapPost("/administration/directory/bind", (UiDirectoryBind i, HttpContext http) => CommandAsync(http, async (db, actor, ct) =>
    {
      var settings = http.RequestServices.GetRequiredService<TenantAdministrationSettings>();
      if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<Guid>();
      if (!i.Reviewed || !settings.Options.DirectoryReadEnabled) return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Review an enabled directory identity before binding it.");
      var reader = http.RequestServices.GetRequiredService<IMicrosoftDirectoryReader>();
      return i.Guest ? await DirectoryUserBindingService.BindExistingGuestAsync(db, actor, reader, settings.Options.TenantId, i.ObjectId, ct) :
        await DirectoryUserBindingService.BindMemberAsync(db, actor, reader, settings.Options.TenantId, i.ObjectId, ct);
    })).WithUiResponse<UiValueResponse<Guid>>(includeConflict: true);
  }
}
