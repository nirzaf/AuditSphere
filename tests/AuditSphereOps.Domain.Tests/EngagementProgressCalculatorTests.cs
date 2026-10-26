using AuditSphereOps.Domain.Practice;

namespace AuditSphereOps.Domain.Tests;

public sealed class EngagementProgressCalculatorTests
{
  private static EngagementProgressStepFact Step(int ordinal, string requirement, string status, bool cancellation = false,
    int unmetDependencies = 0, int unmetEvidence = 0, DateOnly? due = null) =>
    new(Guid.Parse($"00000000-0000-4000-8000-{ordinal:D12}"), $"Step {ordinal}", WorkflowTaskPhases.AuditPlanning, ordinal,
      requirement, status, cancellation, unmetDependencies, unmetEvidence, false, false, null, due);

  [Theory]
  [InlineData(true, null, WorkflowStepRequirements.Mandatory)]
  [InlineData(true, "  ", WorkflowStepRequirements.Mandatory)]
  [InlineData(true, "Only where inventory exists", WorkflowStepRequirements.Conditional)]
  [InlineData(false, null, WorkflowStepRequirements.Optional)]
  public void Classify_DerivesTheRequirementLevelFromTheTemplateStep(bool required, string? condition, string expected) =>
    Assert.Equal(expected, WorkflowStepRequirements.Classify(required, condition));

  [Theory]
  [InlineData("Not applicable: No inventory this period.", true)]
  [InlineData("Not applicable: ", false)]
  [InlineData("Not applicable:", false)]
  [InlineData("Work cancelled for other reasons.", false)]
  [InlineData("not applicable: No inventory this period.", false)]
  [InlineData(null, false)]
  public void Waiver_RequiresAnExplicitSubstantiveReason(string? reason, bool expected) =>
    Assert.Equal(expected, WorkflowStepApplicability.IsExplicitNotApplicableReason(reason));

  [Fact]
  public void Progress_CountsOnlyApplicableRequiredStepsAndReportsOptionalSeparately()
  {
    var figures = EngagementProgressCalculator.Calculate(
    [
      Step(1, WorkflowStepRequirements.Mandatory, PracticeTimeStates.TaskCompleted),
      Step(2, WorkflowStepRequirements.Conditional, PracticeTimeStates.TaskCancelled, cancellation: true),
      Step(3, WorkflowStepRequirements.Mandatory, PracticeTimeStates.TaskOpen, due: new DateOnly(2027, 3, 1)),
      Step(4, WorkflowStepRequirements.Mandatory, PracticeTimeStates.TaskOpen, unmetDependencies: 1, due: new DateOnly(2027, 2, 1)),
      Step(5, WorkflowStepRequirements.Optional, PracticeTimeStates.TaskCompleted),
      Step(6, WorkflowStepRequirements.Optional, PracticeTimeStates.TaskCancelled),
    ], gateOpen: false);

    Assert.Equal((3, 1, 1, 1, 1), (figures.ApplicableRequired, figures.CompletedRequired, figures.PendingRequired,
      figures.BlockedRequired, figures.NotApplicable));
    Assert.Equal((1, 1), (figures.OptionalApplicable, figures.OptionalCompleted));
    Assert.Equal(33, figures.PercentComplete);
    // The next action is the first actionable step; the earliest outstanding due date may belong to a blocked one.
    Assert.Equal(3, figures.NextStep!.Ordinal);
    Assert.Equal(new DateOnly(2027, 2, 1), figures.NextDueDate);
  }

  [Fact]
  public void Progress_NeverWaivesAMandatoryStepOrTrustsStaleEvidence()
  {
    var figures = EngagementProgressCalculator.Calculate(
    [
      Step(1, WorkflowStepRequirements.Mandatory, PracticeTimeStates.TaskCancelled, cancellation: true),
      Step(2, WorkflowStepRequirements.Conditional, PracticeTimeStates.TaskCancelled, cancellation: false),
      Step(3, WorkflowStepRequirements.Mandatory, PracticeTimeStates.TaskCompleted, unmetEvidence: 1),
      Step(4, WorkflowStepRequirements.Mandatory, PracticeTimeStates.TaskCompleted),
    ], gateOpen: false);

    Assert.Equal((4, 1, 3, 0), (figures.ApplicableRequired, figures.CompletedRequired, figures.BlockedRequired, figures.NotApplicable));
    Assert.Equal(25, figures.PercentComplete);
    Assert.Equal("Completed, but its required evidence is no longer current", figures.Steps[2].Condition);
  }

  [Fact]
  public void Progress_IsNeverCompleteWhileAGateIsOpenAndIsUnavailableWithoutAPlan()
  {
    IReadOnlyList<EngagementProgressStepFact> done = [Step(1, WorkflowStepRequirements.Mandatory, PracticeTimeStates.TaskCompleted)];
    Assert.Equal(99, EngagementProgressCalculator.Calculate(done, gateOpen: true).PercentComplete);
    Assert.Equal(100, EngagementProgressCalculator.Calculate(done, gateOpen: false).PercentComplete);
    Assert.Null(EngagementProgressCalculator.Calculate([], gateOpen: false).PercentComplete);
    Assert.Null(EngagementProgressCalculator.Calculate(
      [Step(1, WorkflowStepRequirements.Optional, PracticeTimeStates.TaskCompleted)], gateOpen: false).PercentComplete);
  }
}
