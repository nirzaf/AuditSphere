using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;

namespace AuditSphereOps.E2E.Tests;

// Local, uncommitted design-review harness: seeds a disposable fixture and holds the host for built-in browser review.
public sealed class UxInspectionHarness
{
  [Fact]
  public async Task HoldSeededHostForBrowserInspection()
  {
    var sentinel = Environment.GetEnvironmentVariable("AUDITSPHERE_UX_INSPECT_SENTINEL");
    if (string.IsNullOrEmpty(sentinel)) return;
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "UX-DESIGN-INSPECT");
    var f = host.Fixture;
    var admin = PbcSeed.Actor(f.Admin, "Administrator");
    var partnerUser = PbcSeed.User(f.FirmId, "Staff");
    var managerUser = PbcSeed.User(f.FirmId, "Staff");
    var partner = PbcSeed.Actor(partnerUser, "Partner");
    var financeManager = PbcSeed.Actor(managerUser, "FinanceManager");
    var financeReviewer = PbcSeed.Actor(f.Admin, "FinanceReviewer");
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    await using (var db = host.CreateDbContext())
    {
      db.Users.AddRange(partnerUser, managerUser);
      db.RoleGrants.AddRange(PbcSeed.Grant(f.FirmId, partnerUser, "Partner"), PbcSeed.Grant(f.FirmId, managerUser, "FinanceManager"),
        PbcSeed.Grant(f.FirmId, f.Admin, "Partner"), PbcSeed.Grant(f.FirmId, f.Admin, "FinanceReviewer"));
      await db.SaveChangesAsync();
      var at = DateTimeOffset.UtcNow.AddMinutes(-3);
      const string why = "Owner-directed operator grant: the firm owner instructed full firm-wide privileges for this sole staff account.";
      foreach (var role in new[] { "Partner", "Manager", "Senior", "Staff", "RelationshipManager", "FinanceManager", "FinanceReviewer" })
      {
        db.RoleGrantChangeEvidences.Add(new AuditSphereOps.Domain.Security.RoleGrantChangeEvidence { Id = Guid.NewGuid(), FirmId = f.FirmId,
          TargetUserId = f.Admin.Id, Action = "GRANTED", NewRole = role, Source = "OWNER_DIRECTED_OPERATOR", Reason = why,
          ActorUserId = f.Admin.Id, CreatedAt = at });
        db.Microsoft365AdministrationEvents.Add(new AuditSphereOps.Domain.Microsoft365.Microsoft365AdministrationEvent { Id = Guid.NewGuid(),
          FirmId = f.FirmId, ActorUserId = f.Admin.Id, Operation = "ROLE_GRANTED", TargetUserId = f.Admin.Id, OldState = "-",
          NewState = role + "@FIRM_WIDE", RoleScopeChange = $"NONE -> {role}@FIRM_WIDE", Reason = why, Result = "APPLIED", CreatedAt = at });
      }
      void Event(string op, string old, string next, string reason, string result, int days) =>
        db.Microsoft365AdministrationEvents.Add(new AuditSphereOps.Domain.Microsoft365.Microsoft365AdministrationEvent { Id = Guid.NewGuid(),
          FirmId = f.FirmId, ActorUserId = f.Admin.Id, Operation = op, TargetTenantId = "4de3e6fd-51aa-4ba7-b2c5-82106d2e45f0",
          OldState = old, NewState = next, Reason = reason, Result = result, CreatedAt = DateTimeOffset.UtcNow.AddDays(-days) });
      Event("SELECTED_RESOURCE_BOUNDARY_VERIFIED", "UNVERIFIED", "VERIFIED", "Administrator requested exact resource and unrelated-site denial checks", "exact-resource-and-control-denial", 15);
      Event("CAPABILITIES_VERIFIED", "-", "DIRECTORY_READ,OUTBOUND_MAIL,TENANT_USER_PROVISIONING,GUEST_INVITATION,GROUP_MEMBERSHIP", "Administrator requested capability verification", "RECORDED", 15);
      Event("TENANT_CONSENT_VERIFIED", "IDENTITY_PENDING", "CONSENT_VERIFIED", "Tenant administrator consent and identity verified", "VERIFIED", 15);
      Event("FOLDER_TEMPLATE_APPROVED", "UNAPPROVED", "APPROVED", "Administrator reviewed the immutable folder manifest", "01a12545-91b0-776a-9a22-c0bac362fbe1", 16);
      Event("FOLDER_TEMPLATE_SAVED", "NONE", "89c63840ef603ac98b522adf86d294132c0a3acaf5605c5ecd4cd1bb7cdbc768", "Administrator saved a reviewed folder manifest", "01a12545-91b0-776a-9a22-c0bac362fbe1", 16);
      db.RoleGrantChangeEvidences.Add(new AuditSphereOps.Domain.Security.RoleGrantChangeEvidence { Id = Guid.NewGuid(), FirmId = f.FirmId,
        TargetUserId = f.Admin.Id, Action = "GRANTED", NewRole = "Administrator", Source = "BOOTSTRAP", ActorUserId = f.Admin.Id,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-17) });
      await db.SaveChangesAsync();

      string[] names = ["Al Noor Trading WLL", "Doha Marine Services", "Gulf Bridge Contracting", "Pearl Coast Hospitality",
        "Qatar Fintech Labs", "Sidra Medical Supplies", "Corniche Retail Group", "Lusail Property Holdings"];
      for (var i = 0; i < 28; i++)
        Assert.True((await PracticeCrmService.CreateLeadAsync(db, admin, new($"{names[i % names.Length]} {(i / names.Length) + 1}",
          i % 3 == 0 ? "Email" : "Referral", OwnerUserId: i < 6 ? f.Admin.Id : null))).Succeeded);
      var lead = (await PracticeCrmService.CreateLeadAsync(db, admin, new("Al Wakra Logistics", "Referral"))).Value;
      Assert.True((await PracticeCrmService.QualifyLeadAsync(db, admin, lead)).Succeeded);
      for (var i = 0; i < 12; i++)
        Assert.True((await PracticeCrmService.CreateOpportunityAsync(db, admin, new(lead, i % 4 == 0 ? "AccountingOnly" : "FinancialStatementAudit",
          "Group entity", "2026-01-01", "2026-12-31", 18000m + i * 2500m, i % 3 == 0 ? "USD" : "QAR", Probability: 40m + i * 3,
          OwnerUserId: i % 2 == 0 ? f.Admin.Id : null, NextAction: "Confirm scope and timetable with the CFO"))).Succeeded);

      var rateCard = (await PracticeTimeService.ReviseRateCardAsync(db, admin, new("Staff", "AUDIT", "QAR", 600m))).Value;
      Assert.True((await PracticeTimeService.ApproveRateCardAsync(db, partner, rateCard)).Succeeded);
      var budget = (await PracticeTimeService.ReviseBudgetAsync(db, admin,
        new(f.EngagementId, "QAR", [new("Staff", "AUDIT", 1200, BudgetPhases.Fieldwork)]))).Value;
      Assert.True((await PracticeTimeService.ApproveBudgetAsync(db, partner, budget)).Succeeded);
      var task = (await PracticeTimeService.CreateTaskAsync(db, admin, new("Revenue cut-off testing", f.ClientId, f.EngagementId, f.Staff.Id))).Value;
      var entry = (await PracticeTimeService.SaveTimeDraftAsync(db, PbcSeed.Actor(f.Staff, "Staff"),
        new(task, today, 540, 420, "Staff", "AUDIT", Currency: "QAR"))).Value;
      Assert.True((await PracticeTimeService.SubmitTimeAsync(db, PbcSeed.Actor(f.Staff, "Staff"), entry)).Succeeded);
      Assert.True((await PracticeTimeService.ApproveTimeAsync(db, admin, entry)).Succeeded);
      Assert.True((await PracticeAnalyticsQuery.RecordCostRateAsync(db, partner, f.Staff.Id, 200m, "QAR", today.AddDays(-1))).Succeeded);

      var account = (await BillingService.CreateBillingAccountAsync(db, financeManager, new(f.ClientId, "QAR"))).Value;
      db.FirmFinanceProfiles.Add(new FirmFinanceProfile
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, FunctionalCurrency = "QAR", ProfileKind = BillingStates.TestProfile, Approved = true,
        ApprovedByUserId = f.Admin.Id, ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      var posted = (await BillingService.CreateInvoiceDraftAsync(db, financeManager,
        new(account, "INV-2026-0142", [new InvoiceLineRequest("Statutory audit FY2025 — fieldwork", 2, 12500)], Tax: 0))).Value;
      Assert.True((await BillingService.SubmitInvoiceAsync(db, financeManager, posted)).Succeeded);
      Assert.True((await BillingService.ApproveInvoiceAsync(db, financeReviewer, posted)).Succeeded);
      Assert.True((await BillingService.PostInvoiceAsync(db, financeManager, posted)).Succeeded);
      Assert.True((await BillingService.SendInvoiceAsync(db, financeManager, posted)).Succeeded);
      var receipt = (await BillingService.RecordReceiptAsync(db, financeManager, new(account, 10000, "QNB-TRX-55821", RequestId: Guid.NewGuid()))).Value;
      Assert.True((await BillingService.AllocateReceiptCommandAsync(db, financeManager, new(receipt, posted, 10000, Guid.NewGuid()))).Succeeded);
      Assert.True((await BillingService.CreateInvoiceDraftAsync(db, financeManager,
        new(account, "INV-2026-0143", [new InvoiceLineRequest("Advisory — IFRS 16 transition", 1, 7500)]))).Succeeded);
    }

    var origin = await host.StartApiForIdentityAsync(f.Admin, new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    }, loopbackPort: 5187);
    await File.WriteAllTextAsync(sentinel + ".ready", origin);
    var deadline = DateTime.UtcNow.AddMinutes(180);
    while (!File.Exists(sentinel) && DateTime.UtcNow < deadline) await Task.Delay(1000);
  }
}
