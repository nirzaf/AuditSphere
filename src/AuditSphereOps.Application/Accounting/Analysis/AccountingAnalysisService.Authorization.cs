namespace AuditSphereOps.Application.Accounting;

public static partial class AccountingAnalysisService
{
  private static readonly string[] PreparerRoles = GeneralLedgerQuery.ReadRoles;

  private static readonly string[] ReviewerRoles = ["AccountingReviewer", "Manager", "Partner", "Administrator"];
}
