using System.Reflection;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// Lightweight architecture guards (docs/architecture/auditsphere-architecture-current-architecture.md): the
/// dependency direction between projects is enforced as executable assertions, without
/// an architecture-test framework. Add new rules here rather than introducing a package.
/// </summary>
public sealed class ArchitectureGuardTests
{
  private static HashSet<string> ReferencedProjects(Assembly assembly) =>
    assembly.GetReferencedAssemblies().Select(x => x.Name ?? string.Empty)
      .Where(x => x.StartsWith("AuditSphereOps.", StringComparison.Ordinal))
      .ToHashSet(StringComparer.Ordinal);

  [Fact(DisplayName = "Domain references no outer project")]
  [Trait("Profile", "Architecture")]
  public void DomainReferencesNoOuterProject()
  {
    var referenced = ReferencedProjects(typeof(CommandResult).Assembly);
    Assert.DoesNotContain("AuditSphereOps.Application", referenced);
    Assert.DoesNotContain("AuditSphereOps.Infrastructure", referenced);
    Assert.DoesNotContain("AuditSphereOps.Web", referenced);
    Assert.DoesNotContain("AuditSphereOps.Worker", referenced);
  }

  [Fact(DisplayName = "Application references only Domain of the inner core")]
  [Trait("Profile", "Architecture")]
  public void ApplicationReferencesOnlyDomainOfTheInnerCore()
  {
    var referenced = ReferencedProjects(typeof(ConsolidationService).Assembly);
    Assert.Contains("AuditSphereOps.Domain", referenced);
    Assert.DoesNotContain("AuditSphereOps.Infrastructure", referenced);
    Assert.DoesNotContain("AuditSphereOps.Web", referenced);
    Assert.DoesNotContain("AuditSphereOps.Worker", referenced);
  }

  [Fact(DisplayName = "Infrastructure references Application and Domain, never a host")]
  [Trait("Profile", "Architecture")]
  public void InfrastructureReferencesApplicationAndDomainNeverAHost()
  {
    var referenced = ReferencedProjects(typeof(AuditSphereOps.Infrastructure.Persistence.AuditSphereDbContext).Assembly);
    Assert.Contains("AuditSphereOps.Application", referenced);
    Assert.Contains("AuditSphereOps.Domain", referenced);
    Assert.DoesNotContain("AuditSphereOps.Web", referenced);
    Assert.DoesNotContain("AuditSphereOps.Worker", referenced);
  }
}
