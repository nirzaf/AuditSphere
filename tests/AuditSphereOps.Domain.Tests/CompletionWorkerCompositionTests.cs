using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Domain.Completion;

namespace AuditSphereOps.Domain.Tests;

/// <summary>WU-007: Production worker composition is explicit per live group and never disguised as Acceptance.</summary>
[Trait("Profile", "Unit")]
[Trait("CompletionUnit", "WU-007")]
public sealed class CompletionWorkerCompositionTests
{
  private static readonly HashSet<string> None = [];
  private static readonly OperationDefinition Local = new("ValidateTrialBalance.v1", OperationMode.LOCAL, OperationAuthority.LOCAL_VALIDATION);
  private static readonly OperationDefinition Live = new("live", OperationMode.LIVE, OperationAuthority.LIVE_PROVIDER, Group: "mail");
  private static readonly OperationDefinition Simulated = new("simulated", OperationMode.SIMULATED, OperationAuthority.SIMULATION);

  [Fact]
  public void CoreOnlyProduction_StartsWithExternalEffectsDisabled()
  {
    var decision = WorkerCapabilityProfile.Evaluate("Production", "general", false, false, None);
    Assert.True(decision.Approved);
    Assert.True(decision.SafeCore);
    Assert.False(decision.ExternalEffectsComposed);
    var options = new WorkerOptions(Guid.NewGuid(), "Production", ExternalEffectsEnabled: false, Group: "general");
    options.Validate([Local]);
    Assert.Throws<InvalidOperationException>(() => options.Validate([Live]));
    Assert.Throws<InvalidOperationException>(() => options.Validate([Simulated]));
  }

  [Theory]
  [InlineData("mail", "production-live-group-not-approved:mail")]
  [InlineData("pbc", "production-live-group-not-approved:pbc")]
  [InlineData("client-sites", "production-live-group-not-approved:client-sites")]
  public void ProductionLiveGroup_IsRefusedUntilIndividuallyApproved(string group, string blocker)
  {
    var decision = WorkerCapabilityProfile.Evaluate("Production", group, true, false, new HashSet<string> { "some-other-group" });
    Assert.False(decision.Approved);
    Assert.Contains(blocker, decision.Blockers);
    Assert.False(decision.ExternalEffectsComposed);
  }

  [Fact]
  public void ApprovedProductionMail_ComposesTheLiveProviderButNeverSimulation()
  {
    var approved = new HashSet<string> { "mail" };
    var decision = WorkerCapabilityProfile.Evaluate("Production", "mail", true, false, approved);
    Assert.True(decision.Approved);
    Assert.True(decision.LiveMail);
    new WorkerOptions(Guid.NewGuid(), "Production", ExternalEffectsEnabled: true, Group: "mail").Validate([Live]);

    var simulated = WorkerCapabilityProfile.Evaluate("Production", "mail", true, true, approved);
    Assert.Contains("simulation-adapters-forbidden-in-production", simulated.Blockers);
    Assert.Throws<InvalidOperationException>(() =>
      new WorkerOptions(Guid.NewGuid(), "Production", AllowSimulationAdapters: true, ExternalEffectsEnabled: true, Group: "mail").Validate([Live]));
  }

  [Fact]
  public void InvalidProfiles_RefuseWithExplicitDiagnostics()
  {
    Assert.Contains("external-effects-not-approved-for-group:general",
      WorkerCapabilityProfile.Evaluate("Production", "general", true, false, new HashSet<string> { "general" }).Blockers);
    Assert.Contains("environment-not-approved:Staging", WorkerCapabilityProfile.Evaluate("Staging", "general", false, false, None).Blockers);
    Assert.Contains("worker-group-unknown:payments", WorkerCapabilityProfile.Evaluate("Production", "payments", false, false, None).Blockers);
    Assert.Contains("live-group-requires-external-effects:mail",
      WorkerCapabilityProfile.Evaluate("Production", "mail", false, false, new HashSet<string> { "mail" }).Blockers);
    Assert.Throws<InvalidOperationException>(() => new WorkerOptions(Guid.NewGuid(), "Staging", Group: "general").Validate([Local]));
  }

  [Fact]
  public void Acceptance_KeepsItsExistingNarrowLiveGroupsWithoutAnApprovalList()
  {
    Assert.True(WorkerCapabilityProfile.Evaluate("Acceptance", "mail", true, false, None).LiveMail);
    Assert.True(WorkerCapabilityProfile.Evaluate("Acceptance", "pbc", true, false, None).LivePbc);
    Assert.True(WorkerCapabilityProfile.Evaluate("Acceptance", "client-sites", false, false, None).ClientSitesWorker);
    Assert.False(WorkerCapabilityProfile.Evaluate("Acceptance", "mail", false, false, None).Approved);
    Assert.True(WorkerCapabilityProfile.Evaluate("Acceptance", "records", false, false, None).SafeCore);
  }

  [Fact]
  public void ReleaseCheckpointWorker_IsDefaultOffAndRequiresAnIsolatedLiveComposition()
  {
    var disabled = WorkerCapabilityProfile.Evaluate("Acceptance", ReleaseCheckpointHandler.WorkerGroup,
      externalEffects: true, allowSimulationAdapters: false, None);
    Assert.False(disabled.Approved);
    Assert.Contains("live-audit-release-feature-disabled", disabled.Blockers);

    var acceptance = WorkerCapabilityProfile.Evaluate("Acceptance", ReleaseCheckpointHandler.WorkerGroup,
      externalEffects: true, allowSimulationAdapters: false, None, liveAuditReleaseEnabled: true);
    Assert.True(acceptance.Approved);
    Assert.True(acceptance.LiveReleaseCheckpoint);
    Assert.True(acceptance.ExternalEffectsComposed);
    new WorkerOptions(Guid.NewGuid(), "Acceptance", ExternalEffectsEnabled: true,
      Group: ReleaseCheckpointHandler.WorkerGroup).Validate([ReleaseCheckpointHandler.Descriptor]);

    var productionWithoutApproval = WorkerCapabilityProfile.Evaluate("Production", ReleaseCheckpointHandler.WorkerGroup,
      externalEffects: true, allowSimulationAdapters: false, None, liveAuditReleaseEnabled: true);
    Assert.False(productionWithoutApproval.Approved);
    Assert.Contains($"production-live-group-not-approved:{ReleaseCheckpointHandler.WorkerGroup}", productionWithoutApproval.Blockers);

    var production = WorkerCapabilityProfile.Evaluate("Production", ReleaseCheckpointHandler.WorkerGroup,
      externalEffects: true, allowSimulationAdapters: false,
      new HashSet<string> { ReleaseCheckpointHandler.WorkerGroup }, liveAuditReleaseEnabled: true);
    Assert.True(production.Approved);
    Assert.True(production.LiveReleaseCheckpoint);

    var simulated = WorkerCapabilityProfile.Evaluate("Acceptance", ReleaseCheckpointHandler.WorkerGroup,
      externalEffects: true, allowSimulationAdapters: true, None, liveAuditReleaseEnabled: true);
    Assert.Contains("simulation-adapters-forbidden-for-live-release", simulated.Blockers);
    Assert.False(WorkerCapabilityProfile.Evaluate("Acceptance", ReleaseCheckpointHandler.WorkerGroup,
      externalEffects: false, allowSimulationAdapters: false, None, liveAuditReleaseEnabled: true).Approved);
    Assert.False(WorkerCapabilityProfile.Evaluate("Development", ReleaseCheckpointHandler.WorkerGroup,
      externalEffects: true, allowSimulationAdapters: false, None, liveAuditReleaseEnabled: true).Approved);
  }

  [Fact]
  public void ReleaseCheckpointRuntime_RequiresAnExplicitAbsoluteSharedRootWhenEnabled()
  {
    var disabled = ReleaseCheckpointRuntimeOptions.Resolve(null, "Development", enabled: false);
    Assert.False(disabled.Enabled);
    Assert.True(Path.IsPathFullyQualified(disabled.RootDirectory));
    Assert.Throws<InvalidOperationException>(() => ReleaseCheckpointRuntimeOptions.Resolve(null, "Acceptance", enabled: true));
    Assert.Throws<InvalidOperationException>(() => ReleaseCheckpointRuntimeOptions.Resolve("relative/checkpoints", "Acceptance", enabled: true));
    Assert.Throws<InvalidOperationException>(() => ReleaseCheckpointRuntimeOptions.Resolve("/srv/auditsphere/checkpoints", "Development", enabled: true));

    var api = ReleaseCheckpointRuntimeOptions.Resolve("/srv/auditsphere/checkpoints", "Acceptance", enabled: true);
    var worker = ReleaseCheckpointRuntimeOptions.Resolve("/srv/auditsphere/checkpoints", "Acceptance", enabled: true);
    Assert.True(api.Enabled);
    Assert.Equal(api.RootDirectory, worker.RootDirectory);

    var safety = new ReleaseSafetyOptions();
    safety.Validate(allowSimulationAdapters: false, environmentName: "Acceptance", liveAuditRelease: true);
    Assert.Throws<InvalidOperationException>(() => safety.Validate(false, "Development", liveAuditRelease: true));
    Assert.Throws<InvalidOperationException>(() => safety.Validate(true, "Acceptance", liveAuditRelease: true));
  }
}
