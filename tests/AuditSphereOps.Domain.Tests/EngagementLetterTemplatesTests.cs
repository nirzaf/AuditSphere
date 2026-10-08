using AuditSphereOps.Application.Practice;

namespace AuditSphereOps.Domain.Tests;

public sealed class EngagementLetterTemplatesTests
{
  [Theory]
  [InlineData("FinancialStatementAudit", "EL-STATUTORY-AUDIT-ISA210-v1")]
  [InlineData("InternalAudit", "EL-INTERNAL-AUDIT-v1")]
  [InlineData("AgreedUponProcedures", "EL-AUP-ISRS4400-v1")]
  public void EachSupportedServiceRouteSelectsItsOwnGovernedTemplate(string route, string templateId)
  {
    var template = EngagementLetterTemplates.Resolve(route);

    Assert.NotNull(template);
    Assert.Equal((templateId, route), (template.Id, template.ServiceRoute));
  }

  [Fact]
  public void InternalAuditAndAupNeverShareTemplateWording()
  {
    var internalAudit = EngagementLetterTemplates.Resolve("InternalAudit")!;
    var aup = EngagementLetterTemplates.Resolve("AgreedUponProcedures")!;

    Assert.NotEqual(internalAudit.Id, aup.Id);
    Assert.NotEqual(internalAudit.Heading, aup.Heading);
    Assert.Contains("ISRS 4400", aup.Heading);
    Assert.DoesNotContain("ISRS", internalAudit.Heading);
  }

  [Theory]
  [InlineData("Audit")]        // legacy free-text route that has no governed template
  [InlineData("AccountingOnly")]
  [InlineData("financialstatementaudit")] // routes are matched exactly, never case-folded into a template
  [InlineData("")]
  public void UnsupportedRoutesFailClosedWithoutATemplate(string route)
  {
    Assert.Null(EngagementLetterTemplates.Resolve(route));
  }
}
