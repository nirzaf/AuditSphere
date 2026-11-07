using AuditSphereOps.Api.Ui;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Api.Tests;

public sealed class WorkflowReadHttpStatusTests
{
  [Fact]
  public void GroupPerimeterStaleUsesConflictStatusForUiCommands()
  {
    Assert.Equal(409, UiEndpoints.StatusFor(ErrorCodes.Accounting.GroupPerimeterStale));
  }

  [Theory]
  [InlineData("request.invalid", 400)]
  [InlineData("scope.denied", 403)]
  [InlineData("gate.blocked", 409)]
  [InlineData("revision.stale", 409)]
  [InlineData("generation.stale", 409)]
  [InlineData("protected-state.denied", 409)]
  [InlineData("unexpected.failure", 500)]
  public void WorkflowReadRefusalsKeepTheirHttpMeaning(string errorCode, int expectedStatus)
  {
    Assert.Equal(expectedStatus, UiEndpoints.WorkflowReadFailureStatusCode(errorCode));
  }
}
