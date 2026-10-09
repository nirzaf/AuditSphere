using System.Buffers;
using System.Security.Claims;
using System.Security.Cryptography;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Diagnostics;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using AuditSphereOps.Api.Authentication;
using AuditSphereOps.Api.Diagnostics;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Infrastructure.Providers;

namespace AuditSphereOps.Api;

public static partial class ApiHost
{
  private static void ConfigureProviders(WebApplicationBuilder builder, string connectionString, bool oidcConfigured)
  {
    // Durable operation enqueue boundary for trusted PBC completion (§43.5 item 5). The web host
    // records transfer intents in the durable outbox inside the completion transaction; it never
    // executes provider effects and never claims operations, so no claim registry is registered.
    // The simulated sink is harmless here: simulated execution requires the Test worker
    // composition, and this host only records queue entries.
    builder.Services.AddSingleton<IAuditSphereDbContextFactory, OperationContextFactory>();
    builder.Services.AddHttpClient();
    builder.Services.AddSingleton(new DirectoryCertificateOptions(
      builder.Configuration.GetValue<bool>("DirectoryReader:Enabled"),
      builder.Configuration["Identity:TenantId"] ?? string.Empty,
      builder.Configuration["DirectoryReader:ClientId"] ?? string.Empty,
      builder.Configuration["DirectoryReader:CertificatePath"] ?? string.Empty,
      builder.Configuration["DirectoryReader:PrivateKeyPath"] ?? string.Empty));
    builder.Services.AddTransient<IDirectoryTokenSource>(services =>
      new CertificateDirectoryTokenSource(services.GetRequiredService<IHttpClientFactory>().CreateClient("directory-reader-token"),
        services.GetRequiredService<DirectoryCertificateOptions>()));
    builder.Services.AddTransient<IMicrosoftDirectoryReader>(services =>
      new GraphDirectoryReader(services.GetRequiredService<IHttpClientFactory>().CreateClient("directory-reader"),
        services.GetRequiredService<IDirectoryTokenSource>(),
        services.GetRequiredService<DirectoryCertificateOptions>()));
    builder.Services.AddHttpClient("directory-reader").ConfigurePrimaryHttpMessageHandler(() =>
      new HttpClientHandler { AllowAutoRedirect = false });
    builder.Services.AddHttpClient("directory-reader-token").ConfigurePrimaryHttpMessageHandler(() =>
      new HttpClientHandler { AllowAutoRedirect = false });
    TenantAdministrationComposition.Register(builder);
    builder.Services.AddTransient<ISelectedSiteTokenSource>(services =>
      new CertificateSelectedSiteTokenSource(services.GetRequiredService<IHttpClientFactory>().CreateClient(),
        new SelectedSiteCertificateOptions(
          builder.Configuration["SelectedSite:TenantId"] ?? string.Empty,
          builder.Configuration["SelectedSite:ClientId"] ?? string.Empty,
          builder.Configuration["SelectedSite:CredentialReference"] ?? string.Empty,
          builder.Configuration["SelectedSite:CertificatePath"] ?? string.Empty,
          builder.Configuration["SelectedSite:PrivateKeyPath"] ?? string.Empty)));
    builder.Services.AddTransient<GraphSelectedResourceProbe>(services =>
      new GraphSelectedResourceProbe(services.GetRequiredService<IAuditSphereDbContextFactory>(),
        services.GetRequiredService<IHttpClientFactory>().CreateClient(),
        services.GetRequiredService<ISelectedSiteTokenSource>()));
    builder.Services.AddTransient<ISelectedSiteBoundaryProbe>(services =>
      services.GetRequiredService<GraphSelectedResourceProbe>());
    builder.Services.AddTransient<SelectedSiteBoundaryVerificationService>(services =>
      new SelectedSiteBoundaryVerificationService(services.GetRequiredService<IAuditSphereDbContextFactory>(),
        services.GetRequiredService<ISelectedSiteBoundaryProbe>(),
        builder.Configuration["SelectedSite:NegativeControlSiteUrl"] ?? string.Empty));
    builder.Services.AddTransient<Microsoft365SelectedResourceTestService>(services =>
      new Microsoft365SelectedResourceTestService(services.GetRequiredService<IAuditSphereDbContextFactory>(),
        services.GetRequiredService<GraphSelectedResourceProbe>(),
        builder.Configuration["SelectedSite:NegativeControlSiteUrl"]));
    builder.Services.AddSingleton<IOperationStore, PostgresOperationStore>();
    builder.Services.AddSingleton<GeneralLedgerCompletenessHandler>();
    builder.Services.AddSingleton<FinancialPackageBuildHandler>();
    builder.Services.AddSingleton<FinancialPackageRenderHandler>();
    var externalEffectsEnabled = builder.Configuration.GetValue<bool>("ExternalEffects:Enabled");
    if (externalEffectsEnabled)
    {
      if (string.IsNullOrWhiteSpace(connectionString) || !oidcConfigured ||
          !long.TryParse(builder.Configuration["ExternalEffects:DeploymentEpoch"], out var externalEpoch) || externalEpoch < 1)
        throw new InvalidOperationException("External effects require a connection string, OIDC identity and positive deployment epoch.");
      throw new InvalidOperationException("ExternalEffects:Enabled requires an approved live provider composition; startup refused.");
    }

    var simulationSinkRoot = builder.Configuration["Storage:PbcProviderSimulationRoot"]
      ?? Path.Combine(Path.GetTempPath(), "AuditSphereOps", "pbc-provider-simulation");
    builder.Services.AddSingleton<IPbcProviderSink>(new SimulationPbcProviderSink(simulationSinkRoot));
    // The web host only enqueues transfers; the definition decides which worker may claim them.
    // LiveProvider routes them to the isolated Acceptance "pbc" worker; otherwise they stay simulated.
    var livePbcTransfers = builder.Configuration.GetValue<bool>("PbcTransfer:LiveProvider");
    builder.Services.AddSingleton(sp => new PbcDocumentTransferHandler(sp.GetRequiredService<IAuditSphereDbContextFactory>(),
      sp.GetRequiredService<IPbcProviderSink>(),
      livePbcTransfers ? PbcDocumentTransferHandler.LiveDefinition : PbcDocumentTransferHandler.SimulatedDefinition));
    // Selected-site workspace provisioning uses the same Sites.Selected runtime credential as the document worker.
    var selectedSiteOptions = new SelectedSiteCertificateOptions(
      builder.Configuration["SelectedSite:TenantId"] ?? string.Empty, builder.Configuration["SelectedSite:ClientId"] ?? string.Empty,
      builder.Configuration["SelectedSite:CredentialReference"] ?? string.Empty, builder.Configuration["SelectedSite:CertificatePath"] ?? string.Empty,
      builder.Configuration["SelectedSite:PrivateKeyPath"] ?? string.Empty);
    var selectedSiteConfigured = Guid.TryParse(selectedSiteOptions.TenantId, out _) && Guid.TryParse(selectedSiteOptions.ClientId, out _) &&
      !string.IsNullOrWhiteSpace(selectedSiteOptions.CredentialReference) && File.Exists(selectedSiteOptions.CertificatePath) &&
      File.Exists(selectedSiteOptions.PrivateKeyPath);
    if (livePbcTransfers && !selectedSiteConfigured)
      throw new InvalidOperationException("PbcTransfer:LiveProvider requires the SelectedSite certificate credential.");
    builder.Services.AddSingleton<GraphPreauthenticatedTransport>();
    builder.Services.AddHttpClient("selected-site-graph", client => client.Timeout = TimeSpan.FromSeconds(100))
      .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
    builder.Services.AddTransient<ISelectedSiteWorkspaceProvisioner>(sp => new GraphSelectedSiteDrive(
      sp.GetRequiredService<IHttpClientFactory>().CreateClient("selected-site-graph"), sp.GetRequiredService<ISelectedSiteTokenSource>(),
      sp.GetRequiredService<GraphPreauthenticatedTransport>(), selectedSiteConfigured));

    var checkpointRoot = builder.Configuration["Storage:ReleaseCheckpointRoot"]
      ?? Path.Combine(Path.GetTempPath(), "AuditSphereOps", "release-checkpoints");
    builder.Services.AddSingleton<IReleaseCheckpointStore>(new LocalAppendOnlyCheckpointStore(checkpointRoot));
    builder.Services.AddSingleton<ReleaseCheckpointHandler>();

    var releaseSafety = builder.Configuration.GetSection(ReleaseSafetyOptions.SectionName).Get<ReleaseSafetyOptions>() ?? new();
    releaseSafety.Validate(
      externalEffectsEnabled,
      builder.Configuration.GetValue<bool>("Application:AllowSimulationAdapters"),
      builder.Environment.EnvironmentName,
      builder.Configuration.GetValue<bool>("FeatureActivation:LiveAuditRelease"));
    builder.Services.AddSingleton(releaseSafety);

  }
}
