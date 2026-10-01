using AuditSphereOps.Worker;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Diagnostics;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

// Worker telemetry is independent from provider-effect enablement. The exporter is optional,
// bounded by the OpenTelemetry SDK, and never owns a business transaction or lease decision.
var telemetryEndpoint = TelemetryEndpoint.Parse(builder.Configuration["Telemetry:Otlp:Endpoint"]);
builder.Services.AddOpenTelemetry()
  .ConfigureResource(resource => resource.AddService("AuditSphereOps.Worker"))
  .WithTracing(tracing =>
  {
    tracing.AddSource(AuditDiagnostics.ActivitySourceName)
      .AddSource("Npgsql");
    if (telemetryEndpoint is not null)
      tracing.AddOtlpExporter(options => options.Endpoint = telemetryEndpoint);
  })
  .WithMetrics(metrics =>
  {
    metrics.AddMeter(AuditDiagnostics.MeterName)
      .AddMeter("Npgsql")
      .AddRuntimeInstrumentation();
    if (telemetryEndpoint is not null)
      metrics.AddOtlpExporter(options => options.Endpoint = telemetryEndpoint);
  });
var group = builder.Configuration["Worker:Group"] ?? "general";
var externalEffects = builder.Configuration.GetValue<bool>("ExternalEffects:Enabled");
var liveMail = builder.Environment.IsEnvironment("Acceptance") && externalEffects && group == "mail";
// Live selected-site PBC transfers run only in an isolated Acceptance worker for the "pbc" group.
var livePbc = builder.Environment.IsEnvironment("Acceptance") && externalEffects && group == PbcDocumentTransferHandler.LiveGroup;
var liveClientSites = builder.Environment.IsEnvironment("Acceptance") && externalEffects && group == ClientSharePointSiteHandler.Group && builder.Configuration.GetValue<bool>("ClientSites:Enabled");
if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Test") && !liveMail && !livePbc && !liveClientSites)
  throw new InvalidOperationException("This worker composition is not approved for the current environment.");
var connection = builder.Configuration.GetConnectionString("AuditSphere");
if (string.IsNullOrWhiteSpace(connection))
  throw new InvalidOperationException("ConnectionStrings:AuditSphere is required.");
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connection) { Name = "AuditSphere.Worker" };
var dataSource = dataSourceBuilder.Build();
builder.Services.AddSingleton(dataSource);
builder.Services.AddDbContextFactory<AuditSphereDbContext>(options => options.UseNpgsql(dataSource));
if (!Guid.TryParse(builder.Configuration["Worker:FirmId"], out var firmId))
  throw new InvalidOperationException("Worker:FirmId is required.");
if (!long.TryParse(builder.Configuration["Worker:DeploymentEpoch"], out var deploymentEpoch) || deploymentEpoch < 1)
  throw new InvalidOperationException("Worker:DeploymentEpoch is required and must be positive.");
var workerOptions = new WorkerOptions(firmId, builder.Environment.EnvironmentName,
  builder.Configuration.GetValue<bool>("AllowSimulationAdapters"),
  externalEffects, group,
  DeploymentEpoch: deploymentEpoch);
if (externalEffects && !liveMail && !livePbc && !liveClientSites)
  throw new InvalidOperationException("External effects require an isolated Acceptance mail, pbc or enabled client-sites worker.");

var releaseSafety = builder.Configuration.GetSection(ReleaseSafetyOptions.SectionName).Get<ReleaseSafetyOptions>() ?? new();
releaseSafety.Validate(
  builder.Configuration.GetValue<bool>("ExternalEffects:Enabled"),
  builder.Configuration.GetValue<bool>("AllowSimulationAdapters"),
  builder.Environment.EnvironmentName,
  builder.Configuration.GetValue<bool>("FeatureActivation:LiveAuditRelease"));
builder.Services.AddSingleton(releaseSafety);

var checkpointRoot = builder.Configuration["Storage:ReleaseCheckpointRoot"]
  ?? Path.Combine(Path.GetTempPath(), "AuditSphereOps", "release-checkpoints");
builder.Services.AddSingleton<IReleaseCheckpointStore>(new LocalAppendOnlyCheckpointStore(checkpointRoot));
builder.Services.AddSingleton<ReleaseCheckpointHandler>();

builder.Services.AddSingleton(workerOptions);
builder.Services.AddSingleton<IAuditSphereDbContextFactory, OperationContextFactory>();
builder.Services.AddSingleton<IOperationStore, PostgresOperationStore>();

if (liveMail)
{
  var provider = builder.Configuration["Mail:Provider"]?.Trim().ToUpperInvariant();
  switch (provider)
  {
    case "GRAPH":
      var graph = new GraphMailOptions(
        builder.Configuration["GraphMail:TenantId"] ?? string.Empty,
        builder.Configuration["GraphMail:ClientId"] ?? string.Empty,
        builder.Configuration["GraphMail:SenderMailbox"] ?? string.Empty,
        builder.Configuration["GraphMail:CertificatePath"] ?? string.Empty,
        builder.Configuration["GraphMail:PrivateKeyPath"] ?? string.Empty);
      graph.Validate();
      builder.Services.AddSingleton(graph);
      builder.Services.AddSingleton<IPbcMailSender, GraphPbcMailSender>();
      break;
    case "SMTP":
      var smtp = new SmtpMailOptions(
        builder.Configuration["SmtpMail:Host"] ?? string.Empty,
        builder.Configuration.GetValue<int>("SmtpMail:Port"),
        builder.Configuration.GetValue<bool>("SmtpMail:UseSsl"),
        builder.Configuration["SmtpMail:SenderAddress"] ?? string.Empty,
        builder.Configuration["SmtpMail:SenderName"],
        builder.Configuration["SmtpMail:Username"],
        builder.Configuration["SmtpMail:Password"]);
      smtp.Validate();
      builder.Services.AddSingleton(smtp);
      builder.Services.AddSingleton<IPbcMailSender, SmtpPbcMailSender>();
      break;
    case "RESEND":
      var resend = new ResendMailOptions(
        builder.Configuration["ResendMail:ApiKey"] ?? string.Empty,
        builder.Configuration["ResendMail:SenderAddress"] ?? string.Empty);
      resend.Validate();
      builder.Services.AddSingleton(resend);
      builder.Services.AddSingleton<IPbcMailSender, ResendPbcMailSender>();
      break;
    default:
      throw new InvalidOperationException("Mail:Provider must be Graph, Smtp or Resend.");
  }
  builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
  builder.Services.AddSingleton<PbcMailDeliveryHandler>();
  builder.Services.AddSingleton<IPendingOperationDiscovery>(sp => new PbcMailDiscovery(
    sp.GetRequiredService<IAuditSphereDbContextFactory>(),
    sp.GetRequiredService<IOperationStore>(),
    sp.GetRequiredService<PbcMailDeliveryHandler>(), workerOptions));
  builder.Services.AddSingleton<CommercialMailDeliveryHandler>();
  builder.Services.AddSingleton<IPendingOperationDiscovery>(sp => new CommercialMailDiscovery(
    sp.GetRequiredService<IAuditSphereDbContextFactory>(),
    sp.GetRequiredService<IOperationStore>(),
    sp.GetRequiredService<CommercialMailDeliveryHandler>(), workerOptions));
}
else if (livePbc)
{
  var selectedSite = new SelectedSiteCertificateOptions(
    builder.Configuration["SelectedSite:TenantId"] ?? string.Empty,
    builder.Configuration["SelectedSite:ClientId"] ?? string.Empty,
    builder.Configuration["SelectedSite:CredentialReference"] ?? string.Empty,
    builder.Configuration["SelectedSite:CertificatePath"] ?? string.Empty,
    builder.Configuration["SelectedSite:PrivateKeyPath"] ?? string.Empty);
  selectedSite.Validate();
  var graphHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(100) };
  var tokenHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
  builder.Services.AddSingleton<ISelectedSiteTokenSource>(new CertificateSelectedSiteTokenSource(tokenHttp, selectedSite));
  builder.Services.AddSingleton<GraphPreauthenticatedTransport>();
  builder.Services.AddSingleton(sp => new GraphSelectedSiteDrive(graphHttp, sp.GetRequiredService<ISelectedSiteTokenSource>(),
    sp.GetRequiredService<GraphPreauthenticatedTransport>(), configured: true));
  builder.Services.AddSingleton<PbcRepositoryBindingResolver>();
  builder.Services.AddSingleton<IPbcProviderSink>(sp => new GraphPbcProviderSink(sp.GetRequiredService<PbcRepositoryBindingResolver>(),
    sp.GetRequiredService<GraphSelectedSiteDrive>(), sp.GetRequiredService<IAuditSphereDbContextFactory>()));
  builder.Services.AddSingleton(sp => new PbcDocumentTransferHandler(sp.GetRequiredService<IAuditSphereDbContextFactory>(),
    sp.GetRequiredService<IPbcProviderSink>(), PbcDocumentTransferHandler.LiveDefinition));
  builder.Services.AddSingleton<IPendingOperationDiscovery>(sp => new PbcTransferDiscovery(
    sp.GetRequiredService<IAuditSphereDbContextFactory>(), sp.GetRequiredService<IOperationStore>(),
    sp.GetRequiredService<PbcDocumentTransferHandler>(), workerOptions));
  // Automatic folder provisioning when a Partner activates an engagement (same isolated live group and credential).
  builder.Services.AddSingleton<ISelectedSiteWorkspaceProvisioner>(sp => sp.GetRequiredService<GraphSelectedSiteDrive>());
  builder.Services.AddSingleton(sp => new EngagementWorkspaceProvisioningHandler(sp.GetRequiredService<IAuditSphereDbContextFactory>(),
    sp.GetRequiredService<ISelectedSiteWorkspaceProvisioner>()));
  builder.Services.AddSingleton<IPendingOperationDiscovery>(sp => new EngagementWorkspaceDiscovery(
    sp.GetRequiredService<IAuditSphereDbContextFactory>(), sp.GetRequiredService<IOperationStore>(),
    sp.GetRequiredService<EngagementWorkspaceProvisioningHandler>(), workerOptions));
}
else if (liveClientSites)
{
  var siteOptions = new ClientSiteProviderOptions(builder.Configuration["ClientSites:TenantId"] ?? "",
    builder.Configuration["ClientSites:ClientId"] ?? "", builder.Configuration["ClientSites:SiteHost"] ?? "",
    builder.Configuration["ClientSites:CertificatePath"] ?? "", builder.Configuration["ClientSites:PrivateKeyPath"] ?? "",
    builder.Configuration["SelectedSite:ClientId"] ?? "", builder.Configuration["ClientSites:CustodianObjectId"] ?? "");
  siteOptions.Validate();
  // Reject shared identities even if a token would otherwise satisfy a required role.
  if (!Guid.TryParse(builder.Configuration["Identity:ClientId"], out _))
    throw new InvalidOperationException("Identity:ClientId is required to verify provisioning credential separation.");
  var forbiddenIds = new[] { "Identity:ClientId", "DirectoryReader:ClientId", "TenantConsent:ClientId", "TenantAdministration:Provisioning:ClientId",
    "TenantAdministration:GuestInvitation:ClientId", "TenantAdministration:GroupMembership:ClientId", "TenantAdministration:OutboundMail:ClientId", "GraphMail:ClientId" };
  if (forbiddenIds.Any(key => string.Equals(builder.Configuration[key], siteOptions.ProvisionerClientId, StringComparison.OrdinalIgnoreCase)))
    throw new InvalidOperationException("Client site provisioning must use a separate app identity.");
  if (!Guid.TryParse(builder.Configuration["ClientSites:AdministratorUserId"], out var administrator))
    throw new InvalidOperationException("ClientSites:AdministratorUserId is required.");
  if (!DateTimeOffset.TryParse(builder.Configuration["ClientSites:ClientsCreatedAfter"], System.Globalization.CultureInfo.InvariantCulture,
      System.Globalization.DateTimeStyles.AssumeUniversal, out var cutover))
    throw new InvalidOperationException("ClientSites:ClientsCreatedAfter must be an explicit approved UTC rollout timestamp.");
  var policy = new ClientSitePolicy(true, siteOptions.TenantId, siteOptions.SiteHost, administrator, cutover);
  var readerOptions = new GraphCapabilityCredentialOptions(true, siteOptions.TenantId,
    builder.Configuration["DirectoryReader:ClientId"] ?? "", builder.Configuration["DirectoryReader:CertificatePath"] ?? "",
    builder.Configuration["DirectoryReader:PrivateKeyPath"] ?? "", "User.Read.All");
  readerOptions.Validate();
  var tokenHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
  var siteHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(100) };
  builder.Services.AddSingleton<IClientSharePointSiteProvider>(new SharePointClientSiteProvider(siteHttp, tokenHttp, siteOptions,
    new GraphCapabilityTokenSource(tokenHttp, readerOptions)));
  builder.Services.AddSingleton<ClientSharePointSiteHandler>();
  builder.Services.AddSingleton<IPendingOperationDiscovery>(sp => new ClientSharePointSiteDiscovery(
    sp.GetRequiredService<IAuditSphereDbContextFactory>(), sp.GetRequiredService<IOperationStore>(),
    sp.GetRequiredService<ClientSharePointSiteHandler>(), workerOptions, policy));
}
else
{
  builder.Services.AddSingleton<TrialBalanceValidationHandler>();
  builder.Services.AddSingleton<TrialBalanceDiscovery>();
  builder.Services.AddSingleton<GeneralLedgerCompletenessHandler>();
  builder.Services.AddSingleton<FinancialPackageBuildHandler>();
  builder.Services.AddSingleton<FinancialPackageRenderHandler>();
  var feeAutomation = new AutomaticFeeInvoicePolicy(
    builder.Configuration.GetValue<bool>("AutomaticFeeInvoices:Enabled"),
    builder.Configuration.GetValue<Guid>("AutomaticFeeInvoices:FinanceUserId"),
    builder.Configuration.GetValue<Guid>("AutomaticFeeInvoices:ApprovingAdministratorId"));
  if (feeAutomation.Enabled && (workerOptions.Group != "general" || feeAutomation.FinanceUserId == Guid.Empty || feeAutomation.ApprovingAdministratorId == Guid.Empty))
    throw new InvalidOperationException("Automatic fee invoice drafts require the general worker and explicit finance/administrator identities.");
  builder.Services.AddSingleton(feeAutomation);
  builder.Services.AddSingleton<AutomaticFeeInvoiceHandler>();
  if (feeAutomation.Enabled)
    builder.Services.AddSingleton<IPendingOperationDiscovery>(sp => new AutomaticFeeInvoiceDiscovery(
      sp.GetRequiredService<IAuditSphereDbContextFactory>(), sp.GetRequiredService<IOperationStore>(),
      sp.GetRequiredService<AutomaticFeeInvoiceHandler>(), workerOptions, feeAutomation));
  // Regulatory file freeze: due 60 days after the signed report on the system clock.
  builder.Services.AddSingleton(TimeProvider.System);
  builder.Services.AddSingleton<AuditSphereOps.Application.Records.FileFreezeHandler>();
  builder.Services.AddSingleton<IPendingOperationDiscovery>(sp => new AuditSphereOps.Application.Records.FileFreezeDiscovery(
    sp.GetRequiredService<IAuditSphereDbContextFactory>(), sp.GetRequiredService<IOperationStore>(),
    sp.GetRequiredService<AuditSphereOps.Application.Records.FileFreezeHandler>(), workerOptions, sp.GetRequiredService<TimeProvider>()));
}

// Simulation adapters compose only in a Test environment with the explicit enablement flag.
// In Development the PBC transfer operations remain queued pending an approved provider
// boundary; the durable queue records the truthful pending state instead of executing.
var simulationAllowed = !liveMail && !livePbc && !liveClientSites && workerOptions.EnvironmentName == "Test" && workerOptions.AllowSimulationAdapters;
if (simulationAllowed)
{
  var providerRoot = builder.Configuration["Storage:PbcProviderSimulationRoot"]
    ?? throw new InvalidOperationException("Storage:PbcProviderSimulationRoot is required for simulated transfers.");
  builder.Services.AddSingleton<IPbcProviderSink>(new SimulationPbcProviderSink(providerRoot));
  builder.Services.AddSingleton<PbcDocumentTransferHandler>();
  builder.Services.AddSingleton<IPendingOperationDiscovery>(sp => new PbcTransferDiscovery(
    sp.GetRequiredService<IAuditSphereDbContextFactory>(),
    sp.GetRequiredService<IOperationStore>(),
    sp.GetRequiredService<PbcDocumentTransferHandler>(),
    workerOptions));
}

if (!liveMail && !livePbc && !liveClientSites)
  builder.Services.AddSingleton<IPendingOperationDiscovery>(sp =>
    sp.GetRequiredService<TrialBalanceDiscovery>());
builder.Services.AddSingleton(sp => new DurableOperationRegistry(
  ResolveHandlers(sp, simulationAllowed, liveMail, livePbc, liveClientSites), workerOptions));
builder.Services.AddSingleton<OperationDispatcher>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();

static IOperationHandler[] ResolveHandlers(IServiceProvider sp, bool simulationAllowed, bool liveMail, bool livePbc, bool liveClientSites)
{
  if (liveClientSites) return [sp.GetRequiredService<ClientSharePointSiteHandler>()];
  if (liveMail) return [sp.GetRequiredService<PbcMailDeliveryHandler>(), sp.GetRequiredService<CommercialMailDeliveryHandler>()];
  if (livePbc) return [sp.GetRequiredService<PbcDocumentTransferHandler>(), sp.GetRequiredService<EngagementWorkspaceProvisioningHandler>()];
  var validation = sp.GetRequiredService<TrialBalanceValidationHandler>();
  var completeness = sp.GetRequiredService<GeneralLedgerCompletenessHandler>();
  var packageBuild = sp.GetRequiredService<FinancialPackageBuildHandler>();
  var packageRender = sp.GetRequiredService<FinancialPackageRenderHandler>();
  var checkpoint = sp.GetRequiredService<ReleaseCheckpointHandler>();
  var freeze = sp.GetRequiredService<AuditSphereOps.Application.Records.FileFreezeHandler>();
  var fees = sp.GetRequiredService<AutomaticFeeInvoiceHandler>();
  return simulationAllowed
    ? [validation, completeness, packageBuild, packageRender, checkpoint, freeze, fees, sp.GetRequiredService<PbcDocumentTransferHandler>()]
    : [validation, completeness, packageBuild, packageRender, checkpoint, freeze, fees];
}
