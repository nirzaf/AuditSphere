namespace AuditSphereOps.Application.Audit;

/// <summary>Bounded navigation from a source procedure to existing tools and explicit manual work.</summary>
public sealed record ProcedureSupportLink(string ToolKind, string Label, string? Route, string Explanation, string Availability);

public static class AuditProcedureSupportMap
{
  public const string Available = "AVAILABLE";
  public const string ManualOnly = "MANUAL_ONLY";

  public static IReadOnlyList<ProcedureSupportLink> For(string sourceProcedureId, int? sectionNumber, Guid engagementId,
    Guid? workpaperId)
  {
    var links = new List<ProcedureSupportLink>
    {
      new("WORKSPACE", "Record work in this procedure workspace", null,
        "Use the work performed, conclusion and evidence controls on this page. Opening a tool never completes the procedure.", Available)
    };
    if (workpaperId is { } workpaper)
      links.Add(new("WORKPAPER", "Open the linked workpaper", $"/app/audit/workpapers/{workpaper}",
        "Inspect or edit the exact workpaper linked to this procedure. A workpaper is not the submitted procedure result.", Available));
    var section = sectionNumber ?? 0;
    var engagement = engagementId.ToString();
    switch (section)
    {
      case 1:
        links.Add(new("PLANNING", "Open audit planning", $"/app/engagements/{engagement}/audit-plan",
          "Review the current risk, materiality and programme decisions. Planning is a separate professional workflow.", Available));
        break;
      case 2:
      case 3:
      case 6:
      case 10:
        links.Add(new("CONFIRMATIONS", "Open confirmations", $"/app/engagements/{engagement}/confirmations",
          "Prepare and review the separate confirmation cases. Confirmation completion does not complete this procedure.", Available));
        break;
      case 5:
      case 7:
      case 8:
      case 12:
      case 17:
        links.Add(new("GENERAL_LEDGER", "Open general ledger", $"/app/engagements/{engagement}/general-ledger",
          "Inspect the authorized ledger source and transactions. The auditor still records the procedure conclusion and evidence.", Available));
        break;
      case 11:
      case 15:
      case 18:
        links.Add(new("STATEMENTS", "Open financial statements", $"/app/engagements/{engagement}/statements",
          "Inspect the current statement presentation and supporting lines. The software does not decide the professional conclusion.", Available));
        break;
      case 14:
        links.Add(new("JOURNALS", "Open adjustment journals", "/app/accounting/journals",
          "Review journal evidence and management responses. Journal risk does not replace this procedure's conclusion.", Available));
        break;
      case 19:
        links.Add(new("DIFFERENCES", "Open audit differences", "/app/accounting/differences",
          "Review the aggregate differences and corrections. This procedure still records the auditor's evaluation.", Available));
        break;
      case 16:
      case 20:
        links.Add(new("COMPLETION", "Open engagement completion", $"/app/engagements/{engagement}/completion",
          "Review the current completion evidence and professional decisions. Completion remains a separate authorized human step.", Available));
        break;
      case 4:
        links.Add(new("PHYSICAL_EVIDENCE", "Record physical evidence on this page", null,
          "Register, link and select the physical file above. No automatic inventory count or electronic verification is implied.", Available));
        break;
      case 9:
        links.Add(new("MANUAL_ONLY", "Obtain payroll records manually", null,
          "Payroll execution is not provided. Obtain and review the approved payroll records, calculations and approvals manually.", ManualOnly));
        break;
      case 13:
        links.Add(new("MANUAL_ONLY", "Obtain the statutory return manually", null,
          "Tax filing and statutory computation are not provided. Obtain and review the filed return and computation manually.", ManualOnly));
        break;
      default:
        links.Add(new("MANUAL_ONLY", "Record manual work in this workspace", null,
          "No specialized automated tool is approved for this source procedure. Record the actual work, evidence and conclusion manually.", ManualOnly));
        break;
    }
    return links;
  }
}
