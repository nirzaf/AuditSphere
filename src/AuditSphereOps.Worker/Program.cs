using AuditSphereOps.Worker;
using System.Reflection;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Diagnostics;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Records;
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
var approvedLiveGroups = (builder.Configuration["Worker:ApprovedLiveGroups"] ?? string.Empty)
  .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
var composition = WorkerCapabilityProfile.Evaluate(builder.Environment.EnvironmentName, group, externalEffects,
  builder.Configuration.GetValue<bool>("AllowSimulationAdapters"), approvedLiveGroups);
var liveMail = composition.LiveMail;
var pbcReminders = new PbcReminderOptions(
  builder.Configuration.GetValue<bool>("PbcReminders:Enabled"),
  builder.Configuration.GetValue("PbcReminders:DaysBeforeDue", 3),
  builder.Configuration.GetValue("PbcReminders:OverdueIntervalDays", 3),
  builder.Configuration["PbcReminders:TimeZoneId"] ?? "Asia/Qatar",
  builder.Configuration["PbcReminders:QuietHoursStart"] ?? "21:00",
  builder.Configuration["PbcReminders:QuietHoursEnd"] ?? "07:00",
  builder.Configuration.GetValue("PbcReminders:BatchSize", 50),
  builder.Configuration["PbcReminders:PublicBaseUrl"] ?? string.Empty);
pbcReminders.Validate(liveMail);
// Live selected-site PBC transfers run only in an isolated "pbc" worker (Acceptance, or an approved Production group).
var livePbc = composition.LivePbc;
var clientSitesWorker = composition.ClientSitesWorker;
var clientSitesEnabled = builder.Configuration.GetValue("ClientSites:Enabled", true);
var rawClientSitesBoundary = builder.Configuration["ClientSites:ClientsCreatedAfter"];
var parsedClientSitesBoundary = DateTimeOffset.TryParse(rawClientSitesBoundary, System.Globalization.CultureInfo.InvariantCulture,
  System.Globalization.DateTimeStyles.RoundtripKind, out var clientSitesBoundary);
var validClientSitesBoundary = parsedClientSitesBoundary && clientSitesBoundary.Offset == TimeSpan.Zero;
var clientSiteRolloutPolicy = new ClientSiteRolloutPolicy(clientSitesEnabled,
  validClientSitesBoundary ? clientSitesBoundary.ToUniversalTime() : null,
  clientSitesEnabled && !string.IsNullOrWhiteSpace(rawClientSitesBoundary) && !validClientSitesBoundary);
var liveClientSites = false;
var clientSitesReadiness = new ClientSiteWorkerReadiness(
  !clientSitesEnabled ? "DISABLED" : "BLOCKED_CONFIGURATION",
  !clientSitesEnabled ? "client-site-policy-disabled" : "client-site-worker-configuration-pending");
if (!composition.Approved)
  throw new InvalidOperationException("This worker composition is not approved for the current environment: " +
    string.Join(", ", composition.Blockers.DefaultIfEmpty("no-approved-worker-group")));
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
if (externalEffects && !liveMail && !livePbc && !clientSitesWorker)
  throw new InvalidOperationException("External effects require an isolated mail, pbc or enabled client-sites worker.");

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
builder.Services.AddSingleton(pbcReminders);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(clientSiteRolloutPolicy);
builder.Services.AddSingleton<IAuditSphereDbContextFactory, OperationContextFactory>();
builder.Services.AddSingleton<IOperationStore, PostgresOperationStore>();

if (liveMail)
{
  if (pbcReminders.Enabled)
    builder.Services.AddSingleton<IPendingOperationDiscovery>(sp => new PbcReminderDiscovery(
      sp.GetRequiredService<IAuditSphereDbContextFactory>(), workerOptions, pbcReminders,
      sp.GetRequiredService<TimeProvider>()));
  var provider = builder.Configuration["Mail:Provider"]?.Trim().ToUpperInvariant();
  switch (provider)
  {
    case "GRAPH":
      var graph = new GraphMailOptions(
        builder.Configuration["GraphMail:TenantId"] ?? string.Empty,
        builder.Configuration["GraphMail:ClientId"] ?? string.Empty,
        builder.Configuration["GraphMail:SenderObjectId"] ?? string.Empty,
        builder.Configuration["GraphMail:CertificatePath"] ?? string.Empty,
        builder.Configuration["GraphMail:PrivateKeyPath"] ?? string.Empty,
        workerOptions.FirmId);
      graph.Validate();
      var observedRepositoryCommit = ReadBuildRepositoryCommit();
      var mailAuthorization = ReadMailAuthorizationRuntimeOptions(
        builder.Configuration, workerOptions.FirmId, observedRepositoryCommit);
      var evidenceFiles = new MailAuthorizationAttestationFileSourceOptions(
        builder.Configuration["MailAuthorization:Evidence:CanonicalPayloadPath"] ?? string.Empty,
        builder.Configuration["MailAuthorization:Evidence:DetachedCmsPath"] ?? string.Empty);
      evidenceFiles.Validate();
      if (!Guid.TryParse(graph.TenantId, out var graphTenantId) || graphTenantId != mailAuthorization.TenantId ||
          !Guid.TryParse(graph.ClientId, out var graphClientId) || graphClientId != mailAuthorization.RuntimeClientId ||
          !Guid.TryParse(graph.SenderObjectId, out var graphSenderId) ||
          !mailAuthorization.SenderMailboxObjectIds.Contains(graphSenderId))
        throw new InvalidOperationException("Graph mail identity does not match the signed-evidence binding.");
      builder.Services.AddSingleton(graph);
      builder.Services.AddSingleton(mailAuthorization);
      builder.Services.AddSingleton(evidenceFiles);
      builder.Services.AddSingleton<IMailAuthorizationAttestationSource, MailAuthorizationAttestationFileSource>();
      builder.Services.AddSingleton<IMailAuthorizationRuntimeGate, MailAuthorizationRuntimeGate>();
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
  builder.Services.AddSingleton(provider == "GRAPH"
    ? new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
      { Timeout = TimeSpan.FromSeconds(30) }
    : new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
  builder.Services.AddSingleton<PbcMailDeliveryHandler>(sp => new PbcMailDeliveryHandler(
    sp.GetRequiredService<IAuditSphereDbContextFactory>(),
    sp.GetRequiredService<IPbcMailSender>(),
    sp.GetRequiredService<PbcReminderOptions>(),
    sp.GetRequiredService<TimeProvider>()));
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
    sp.GetRequiredService<ISelectedSiteWorkspaceProvisioner>(), sp.GetRequiredService<ClientSiteRolloutPolicy>()));
  builder.Services.AddSingleton<IPendingOperationDiscovery>(sp => new EngagementWorkspaceDiscovery(
    sp.GetRequiredService<IAuditSphereDbContextFactory>(), sp.GetRequiredService<IOperationStore>(),
    sp.GetRequiredService<EngagementWorkspaceProvisioningHandler>(), workerOptions));
}
else if (clientSitesWorker)
{
  if (clientSitesEnabled && externalEffects)
  {
    try
    {
      var siteOptions = new ClientSiteProviderOptions(builder.Configuration["ClientSites:TenantId"] ?? "",
        builder.Configuration["ClientSites:ClientId"] ?? "", builder.Configuration["ClientSites:SiteHost"] ?? "",
        builder.Configuration["ClientSites:CertificatePath"] ?? "", builder.Configuration["ClientSites:PrivateKeyPath"] ?? "",
        builder.Configuration["SelectedSite:ClientId"] ?? "", builder.Configuration["ClientSites:CustodianObjectId"] ?? "");
      siteOptions.Validate();
      // Reject shared identities even if a token would otherwise satisfy a required role.
      if (!Guid.TryParse(builder.Configuration["Identity:ClientId"], out _))
        throw new InvalidOperationException("client-site-identity-separation-required");
      var forbiddenIds = new[] { "Identity:ClientId", "DirectoryReader:ClientId", "TenantConsent:ClientId", "TenantAdministration:Provisioning:ClientId",
        "TenantAdministration:GuestInvitation:ClientId", "TenantAdministration:GroupMembership:ClientId", "TenantAdministration:OutboundMail:ClientId", "GraphMail:ClientId" };
      if (forbiddenIds.Any(key => string.Equals(builder.Configuration[key], siteOptions.ProvisionerClientId, StringComparison.OrdinalIgnoreCase)))
        throw new InvalidOperationException("client-site-identity-separation-required");
      if (!Guid.TryParse(builder.Configuration["ClientSites:AdministratorUserId"], out var administrator))
        throw new InvalidOperationException("client-site-administrator-required");
      if (clientSiteRolloutPolicy.BoundaryInvalid || clientSiteRolloutPolicy.ClientsCreatedAfter is not DateTimeOffset cutover)
        throw new InvalidOperationException("client-site-rollout-boundary-required");
      var policy = new ClientSitePolicy(true, siteOptions.TenantId, siteOptions.SiteHost, administrator, cutover.ToUniversalTime());
      var readerOptions = new GraphCapabilityCredentialOptions(true, siteOptions.TenantId,
        builder.Configuration["DirectoryReader:ClientId"] ?? "", builder.Configuration["DirectoryReader:CertificatePath"] ?? "",
        builder.Configuration["DirectoryReader:PrivateKeyPath"] ?? "", "User.Read.All");
      readerOptions.Validate();
      var tokenHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
      var siteHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(100) };
      var provider = new SharePointClientSiteProvider(siteHttp, tokenHttp, siteOptions,
        new GraphCapabilityTokenSource(tokenHttp, readerOptions));
      builder.Services.AddSingleton<IClientSharePointSiteProvider>(provider);
      builder.Services.AddSingleton<IClientSharePointArchiveCopyProvider>(provider);
      builder.Services.AddSingleton<ClientSharePointSiteHandler>();
      builder.Services.AddSingleton<ArchiveCopyHandler>();
      builder.Services.AddSingleton<IPendingOperationDiscovery>(sp => new ClientSharePointSiteDiscovery(
        sp.GetRequiredService<IAuditSphereDbContextFactory>(), sp.GetRequiredService<IOperationStore>(),
        sp.GetRequiredService<ClientSharePointSiteHandler>(), workerOptions, policy));
      clientSitesReadiness = new("READY", null);
      liveClientSites = true;
    }
    catch (Exception ex) when (ex is OperationBlockedException or InvalidOperationException or ArgumentException or IOException)
    {
      var diagnostic = ex switch
      {
        OperationBlockedException blocked => blocked.Code,
        InvalidOperationException invalid when invalid.Message.StartsWith("client-site-", StringComparison.Ordinal) => invalid.Message,
        _ => "client-site-worker-configuration-invalid"
      };
      clientSitesReadiness = new("BLOCKED_CONFIGURATION", diagnostic);
    }
  }
  else if (clientSitesEnabled)
  {
    clientSitesReadiness = new("BLOCKED_CONFIGURATION", "client-site-external-effects-disabled");
  }
  if (clientSitesWorker)
  {
    builder.Services.AddSingleton(clientSitesReadiness);
    builder.Services.AddHostedService<ClientSiteWorkerHeartbeatService>();
  }
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
  var workflowReminders = new WorkflowReminderOptions(
    builder.Configuration.GetValue<bool>("WorkflowExperience:Enabled") &&
      builder.Configuration.GetValue<bool>("WorkflowReminders:Enabled"),
    builder.Configuration.GetValue("WorkflowReminders:BatchSize", 25));
  workflowReminders.Validate();
  builder.Services.AddSingleton(workflowReminders);
  builder.Services.AddSingleton<WorkflowReminderTaskHandler>();
  builder.Services.AddSingleton<IPendingOperationDiscovery>(sp => new WorkflowReminderDiscovery(
    sp.GetRequiredService<IAuditSphereDbContextFactory>(), sp.GetRequiredService<IOperationStore>(),
    sp.GetRequiredService<WorkflowReminderTaskHandler>(), workerOptions, workflowReminders,
    sp.GetRequiredService<TimeProvider>()));
  if (feeAutomation.Enabled)
    builder.Services.AddSingleton<IPendingOperationDiscovery>(sp => new AutomaticFeeInvoiceDiscovery(
      sp.GetRequiredService<IAuditSphereDbContextFactory>(), sp.GetRequiredService<IOperationStore>(),
      sp.GetRequiredService<AutomaticFeeInvoiceHandler>(), workerOptions, feeAutomation));
  // Regulatory file freeze: due 60 days after the signed report on the system clock.
  builder.Services.AddSingleton<AuditSphereOps.Application.Records.FileFreezeHandler>();
  builder.Services.AddSingleton<IPendingOperationDiscovery>(sp => new AuditSphereOps.Application.Records.FileFreezeDiscovery(
    sp.GetRequiredService<IAuditSphereDbContextFactory>(), sp.GetRequiredService<IOperationStore>(),
    sp.GetRequiredService<AuditSphereOps.Application.Records.FileFreezeHandler>(), workerOptions, sp.GetRequiredService<TimeProvider>()));
}

// Simulation adapters compose only in a Test environment with the explicit enablement flag.
// In Development the PBC transfer operations remain queued pending an approved provider
// boundary; the durable queue records the truthful pending state instead of executing.
var simulationAllowed = !liveMail && !livePbc && !liveClientSites && !clientSitesWorker && workerOptions.EnvironmentName == "Test" && workerOptions.AllowSimulationAdapters;
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

if (!liveMail && !livePbc && !liveClientSites && !clientSitesWorker)
  builder.Services.AddSingleton<IPendingOperationDiscovery>(sp =>
    sp.GetRequiredService<TrialBalanceDiscovery>());
builder.Services.AddSingleton(sp => new DurableOperationRegistry(
  ResolveHandlers(sp, simulationAllowed, liveMail, livePbc, liveClientSites, clientSitesWorker), workerOptions));
builder.Services.AddSingleton<OperationDispatcher>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();

static IOperationHandler[] ResolveHandlers(IServiceProvider sp, bool simulationAllowed, bool liveMail, bool livePbc, bool liveClientSites, bool clientSitesWorker)
{
  if (clientSitesWorker) return liveClientSites
    ? [sp.GetRequiredService<ClientSharePointSiteHandler>(), sp.GetRequiredService<ArchiveCopyHandler>()]
    : [];
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
  var reminders = sp.GetRequiredService<WorkflowReminderTaskHandler>();
  return simulationAllowed
    ? [validation, completeness, packageBuild, packageRender, checkpoint, freeze, fees, reminders, sp.GetRequiredService<PbcDocumentTransferHandler>()]
    : [validation, completeness, packageBuild, packageRender, checkpoint, freeze, fees, reminders];
}

static MailAuthorizationRuntimeOptions ReadMailAuthorizationRuntimeOptions(IConfiguration configuration, Guid firmId,
  string observedRepositoryCommit)
{
  var section = configuration.GetSection("MailAuthorization");
  var model = section["Model"]?.Trim().ToUpperInvariant() switch
  {
    "LEGACY" => MailAuthorizationModel.LegacyApplicationAccessPolicy,
    "EXCHANGE_RBAC" => MailAuthorizationModel.ExchangeRbacResourceScope,
    _ => throw new InvalidOperationException("MailAuthorization:Model must be LEGACY or EXCHANGE_RBAC.")
  };

  Guid RequiredGuid(string key) => Guid.TryParse(section[key], out var value) && value != Guid.Empty
    ? value
    : throw new InvalidOperationException($"MailAuthorization:{key} must be a non-empty GUID.");
  string RequiredString(string key) => section[key] ?? string.Empty;

  var senderValues = section.GetSection("SenderMailboxObjectIds").Get<string[]>() ?? [];
  var senders = senderValues.Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty).ToArray();
  var source = new MailAuthorizationSourceIdentity(
    RequiredString("Source:RepositoryCommit"),
    RequiredString("Source:ConfigureMailAccessSha256"),
    RequiredString("Source:MailAccessModuleSha256"),
    RequiredString("Source:ModulesLockSha256"),
    RequiredString("Source:CommonModuleSha256"),
    RequiredString("Source:EntraModuleSha256"),
    RequiredString("Source:PlanSchemaSha256"));
  var options = new MailAuthorizationRuntimeOptions(
    firmId, RequiredGuid("TenantId"), model, RequiredGuid("RuntimeClientId"),
    RequiredGuid("EnterpriseServicePrincipalObjectId"), senders,
    RequiredGuid("ControlMailboxObjectId"), RequiredString("PlanSha256"), source,
    observedRepositoryCommit,
    RequiredString("PinnedSignerCertificateSha256"),
    TimeSpan.FromHours(section.GetValue("MaximumEvidenceAgeHours", 24.0)));
  options.Validate();
  return options;
}

static string ReadBuildRepositoryCommit()
{
  var assemblies = new[]
  {
    typeof(Worker).Assembly,
    typeof(MailAuthorizationRuntimeOptions).Assembly,
    typeof(MailAuthorizationAttestationFileSource).Assembly
  };
  var commits = assemblies.Select(ReadAssemblyRepositoryCommit).ToArray();
  if (commits.Distinct(StringComparer.Ordinal).Count() != 1)
    throw new InvalidOperationException("Mail authorization requires matching build source metadata across Worker, Application, and Infrastructure.");
  return commits[0];
}

static string ReadAssemblyRepositoryCommit(Assembly assembly)
{
  var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
  var separator = informationalVersion?.LastIndexOf('+') ?? -1;
  if (separator < 0 || separator == informationalVersion!.Length - 1)
    throw new InvalidOperationException("Mail authorization requires full build source metadata on Worker, Application, and Infrastructure.");
  var commit = informationalVersion[(separator + 1)..];
  if (commit.Length != 40 || commit.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
    throw new InvalidOperationException("Mail authorization requires full build source metadata on Worker, Application, and Infrastructure.");
  return commit;
}
