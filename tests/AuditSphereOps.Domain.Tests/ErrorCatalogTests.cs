using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Domain.Tests;

public sealed class ErrorCatalogTests
{
  [Fact]
  public void Unknown_codes_use_a_safe_fallback()
  {
    Assert.Equal(ErrorCatalog.UnknownUiMessage, ErrorCatalog.ForUi("future.server.detail"));
    Assert.Equal(ErrorCatalog.UnknownUiMessage, ErrorCatalog.ForUi(null));
  }

  [Fact]
  public void Stable_codes_have_non_sensitive_ui_messages()
  {
    Assert.Equal("The draft could not be saved because it changed.",
      ErrorCatalog.ForUi(ErrorCodes.Drafts.Conflict));
    Assert.Equal("This operation is currently blocked by a required prerequisite.",
      ErrorCatalog.ForUi(ErrorCodes.GateBlocked));
  }
}
