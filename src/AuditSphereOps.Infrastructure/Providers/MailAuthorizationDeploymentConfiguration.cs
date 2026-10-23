using System.Reflection;
using AuditSphereOps.Application.Microsoft365;
using Microsoft.Extensions.Configuration;

namespace AuditSphereOps.Infrastructure.Providers;

/// <summary>Shared non-secret mail binding and actual build-source checks for API and worker hosts.</summary>
public static class MailAuthorizationDeploymentConfiguration
{
  public static MailAuthorizationRuntimeOptions Read(IConfiguration configuration, Guid firmId,
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

  public static string ReadBuildRepositoryCommit(params Assembly[] assemblies)
  {
    if (assemblies.Length < 3) throw new InvalidOperationException("Mail authorization requires host, Application and Infrastructure build identities.");
    var commits = assemblies.Select(ReadAssemblyRepositoryCommit).ToArray();
    if (commits.Distinct(StringComparer.Ordinal).Count() != 1)
      throw new InvalidOperationException("Mail authorization requires matching build source metadata across the host, Application and Infrastructure.");
    return commits[0];
  }

  private static string ReadAssemblyRepositoryCommit(Assembly assembly)
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
}
