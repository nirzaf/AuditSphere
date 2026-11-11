using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NativeSourceWorker = AuditSphereOps.Worker.Worker;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class AuditDeliverablesTests
{
  internal sealed record World(Guid FirmId, Guid ClientId, Guid EngagementId, Dictionary<string, AppUser> U, Guid ProcedureId, Guid ResultId, string ProcedureCode, long InputGeneration)
  {
    public ActorContext A(string name, params string[] roles) => new(U[name].Id, FirmId, U[name].SessionEpoch, roles);
  }
  internal static async Task<World> SeedSupportedDeliverablesAsync(ITestPostgresDatabase pg, bool refreshPortalRisk = true, bool requireCommercialReview = false, bool prepareHoldingRecipient = false, string clientLegalName = "Synthetic R02 Audit Client")
  {
    var scope = await SupportedAuditCompletionWorldBuilder.CreateAsync(pg, primaryFee: 25000m, includeSibling: false, requireCommercialReview: requireCommercialReview, clientLegalName: clientLegalName);
    var admin = scope.Administrator;
    var firmId = scope.Primary.FirmId;
    var clientId = scope.Primary.ClientId;
    var engagementId = scope.Primary.EngagementId;
    var users = new Dictionary<string, AppUser>();
    await using var db = new AuditSphereDbContext(pg.Options);
    var administrator = await db.Users.AsNoTracking().SingleAsync(x => x.Id == admin.UserId);
    foreach (var (name, role) in new[] { ("partner", "Partner"), ("partner2", "Partner"),
      ("manager", "Manager"), ("senior", "Senior"), ("associate", "Staff"), ("associate2", "Staff"), ("client", "ClientUser") })
    {
      var ensured = await RoleAdministrationService.EnsureUserAsync(db, admin,
        new(administrator.TenantId, Guid.NewGuid().ToString("D"), $"{name}-{Guid.NewGuid():N}@example.test",
          name, name == "client" ? "Client" : "Staff", "APPROVED_ROSTER"));
      Assert.True(ensured.Succeeded, ensured.Message);
      // Non-partner staff gain only their staffing-derived engagement authority below.
      if (role == "Partner")
      {
        var grant = await RoleAdministrationService.ApplyRoleGrantAsync(db, admin,
          new(ensured.Value, role, "FIRM_WIDE"));
        Assert.True(grant.Succeeded, grant.Message);
      }
      else if (role == "ClientUser")
      {
        var grant = await RoleAdministrationService.ApplyRoleGrantAndInvitationAsync(db, admin,
          new(ensured.Value, role, "ENGAGEMENT", clientId, engagementId));
        Assert.True(grant.Succeeded, grant.Message);
        Assert.Equal("NOT_SENT", grant.Value!.DeliveryState);
      }
      users[name] = await db.Users.AsNoTracking().SingleAsync(x => x.Id == ensured.Value);
    }
    users["administrator"] = administrator;
    foreach (var (name, role) in new[] { ("accounting-preparer", "AccountingPreparer"), ("accounting-reviewer", "AccountingReviewer") })
    {
      var actor = await SupportedAuditCompletionWorldBuilder.ProvisionFirmRoleAsync(pg, (firmId, admin), role);
      users[name] = actor.User;
    }
    foreach (var (name, role) in new[] { ("finance-maker", "FinanceManager"), ("finance-reviewer", "FinanceReviewer") })
      users[name] = await (from user in db.Users.AsNoTracking() join grant in db.RoleGrants.AsNoTracking() on user.Id equals grant.UserId
        where grant.FirmId == firmId && grant.Role == role && grant.ClientId == null && grant.RevokedAt == null select user).SingleAsync();
    ActorContext Actor(string name, string role) => new(users[name].Id, firmId, users[name].SessionEpoch, [role]);
    var partner = Actor("partner", "Partner");
    var partner2 = Actor("partner2", "Partner");
    var senior = Actor("senior", "Senior");
    var associate = Actor("associate", "Staff");
    foreach (var name in new[] { "partner", "manager" })
    {
      var certification = await ResourcePlanningService.AddCertificationAsync(db, partner2,
        new(users[name].Id, "ACCA", "Synthetic qualification evidence", null));
      Assert.True(certification.Succeeded, certification.Message);
      var review = await ResourcePlanningService.GetCertificationReviewAsync(db, admin, certification.Value);
      Assert.True(review.Succeeded, review.Message);
      var verified = await ResourcePlanningService.ReviewCertificationAsync(db, admin,
        new(certification.Value, Guid.NewGuid(), review.Value!.SourceDigest, review.Value.Revision,
          "VERIFIED", "synthetic-evidence:qualification", "Synthetic independent qualification verification"));
      Assert.True(verified.Succeeded, verified.Message);
    }
    foreach (var (name, level) in new[] { ("partner", StaffingLevels.EngagementPartner), ("manager", StaffingLevels.AuditManager),
      ("senior", StaffingLevels.SeniorAuditor), ("associate", StaffingLevels.StaffAssociate), ("associate2", StaffingLevels.StaffAssociate) })
    {
      var assigned = await StaffingService.AssignAsync(db, partner2, new(engagementId, users[name].Id, level));
      Assert.True(assigned.Succeeded, $"{name}: {assigned.Message}");
    }
    var firstSignIn = await ClientPortalService.CompleteFirstSignInAsync(db, Actor("client", "ClientUser"), true);
    Assert.True(firstSignIn.Succeeded, firstSignIn.Message);
    var finding = await AuditPlanningService.CreateFindingAsync(db, senior,
      new(engagementId, "Control deficiency", "Credit notes approved without review", false, 12000m,
        "Approval workflow introduced from March."));
    Assert.True(finding.Succeeded, finding.Message);

    // Contact creation advances client input generation, so it precedes all planning/source evidence.
    await CompletionBundleFixture.PrepareSupportedBundleContactAsync(pg.Options, new(firmId, clientId, engagementId, users));
    if (prepareHoldingRecipient)
    {
      var recipient = await PracticeCrmService.CreateClientContactAsync(db, partner,
        new(clientId, "Client MD", "md@client.example.test", "Managing Director", Primary: true));
      Assert.True(recipient.Succeeded, recipient.Message);
    }
    await EstablishSupportedDeliverablesPlanningAsync(pg, scope, partner2, partner);
    if (refreshPortalRisk)
      await RefreshSupportedPortalRiskAsync(pg, new(firmId, clientId, engagementId, users, Guid.Empty, Guid.Empty, "", 0));
    var generation = await db.ClientSafetyStates.AsNoTracking().Where(x => x.Id == clientId && x.FirmId == firmId)
      .Select(x => x.InputGeneration).SingleAsync();
    var prepared = await AuditProgramService.PrepareControlledAuditProgramAsync(db, partner2,
      new("2026.1", AuditProgramCatalog.SourceHash));
    Assert.True(prepared.Succeeded, prepared.Message);
    var published = await AuditProgramService.ReviewControlledAuditProgramPublicationAsync(db, partner,
      new(prepared.Value!.ProgramVersionId, prepared.Value.ContentDigest!, "synthetic-evidence:controlled-program-review"));
    Assert.True(published.Succeeded, published.Message);
    var adopted = await AuditProgramService.AdoptAsync(db, partner,
      new(engagementId, published.Value!.ProgramVersionId, published.Value.SourceHash, published.Value.ContentDigest));
    Assert.True(adopted.Succeeded, adopted.Message);
    var procedures = await db.AuditProcedures.AsNoTracking().Where(x => x.EngagementId == engagementId)
      .OrderBy(x => x.SourceProcedureId).ToListAsync();
    var selected = procedures.Single(x => x.SourceProcedureId == "AWP-05-08");
    foreach (var procedure in procedures)
    {
      var applies = procedure.Id == selected.Id;
      var proposed = await AuditProgramService.DecideApplicabilityAsync(db, senior,
        new(procedure.Id, applies ? AuditApplicabilityStatuses.Applicable : AuditApplicabilityActions.ProposeNotApplicable,
          applies ? null : $"Synthetic revenue cut-off fixture: {procedure.SourceProcedureId} concerns an excluded work area.",
          procedure.CurrentApplicabilityRevision, procedure.CurrentDefinitionRevision, Guid.NewGuid()));
      Assert.True(proposed.Succeeded, proposed.Message);
      if (!applies)
      {
        var reviewed = await AuditProgramService.DecideApplicabilityAsync(db, partner,
          new(procedure.Id, AuditApplicabilityActions.ApproveNotApplicable,
            "Synthetic independent review of the bounded revenue cut-off fixture scope.",
            proposed.Value!.ApplicabilityRevision, procedure.CurrentDefinitionRevision, Guid.NewGuid(), proposed.Value.DecisionId));
        Assert.True(reviewed.Succeeded, reviewed.Message);
      }
    }
    var submitted = await AuditProgramService.SubmitResultAsync(db, associate,
      new(selected.Id, generation, "Selected 25 invoices around year end and agreed them to dispatch notes.",
        "{}", ["synthetic-evidence:revenue-cut-off"], "Revenue is recorded in the correct period."));
    Assert.True(submitted.Succeeded, submitted.Message);
    return new(firmId, clientId, engagementId, users, selected.Id, submitted.Value!.AuditProcedureResultId,
      selected.SourceProcedureId, generation);
  }

  private static async Task<Guid> CreateSupportedDispatchedConfirmationAsync(AuditSphereDbContext db, World w,
    string area, string source, decimal amount, string respondent, string validation, string dispatch)
  {
    var made = await AuditFieldworkService.CreateConfirmationAsync(db, w.A("senior", "Senior"),
      new(w.EngagementId, null, area, source, amount, "QAR", new DateOnly(2026, 12, 31), respondent, validation));
    Assert.True(made.Succeeded, made.Message);
    var id = made.Value!.ConfirmationCaseId;
    var approved = await AuditFieldworkService.ApproveConfirmationAsync(db, w.A("manager", "Manager"), id);
    Assert.True(approved.Succeeded, approved.Message);
    var sent = await AuditFieldworkService.RecordDispatchEvidenceAsync(db, w.A("senior", "Senior"), new(id, dispatch));
    Assert.True(sent.Succeeded, sent.Message);
    return id;
  }

  // Explicit fixture preparation after accepted new inputs. No letter is regenerated or rebound.
  internal static async Task RefreshSupportedPortalRiskAsync(ITestPostgresDatabase pg, World w)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var letters = await db.CommercialDocuments.AsNoTracking().Where(x => x.FirmId == w.FirmId &&
      x.Kind == CommercialDocumentKinds.EngagementLetter).OrderBy(x => x.Id).ToListAsync();
    Assert.Single(letters);
    var client = w.A("client", "ClientUser");
    var request = new AuthorizationRequest(w.FirmId, w.ClientId, w.EngagementId, RequiredRoles: ["ClientUser"]);
    var stale = await AuthorizationDecision.AuthorizeAsync(db, client, request);
    Assert.Equal(ErrorCodes.GateBlocked, stale.ErrorCode);
    await SupportedAuditCompletionWorldBuilder.RecordUnconditionalAcceptanceAsync(pg, w.ClientId,
      "FinancialStatementAudit", w.A("partner2", "Partner"), w.A("partner", "Partner"), w.EngagementId);
    var current = await AuthorizationDecision.AuthorizeAsync(db, client, request);
    Assert.True(current.Succeeded, current.Message);
    var retained = await db.CommercialDocuments.AsNoTracking().Where(x => x.FirmId == w.FirmId &&
      x.Kind == CommercialDocumentKinds.EngagementLetter).OrderBy(x => x.Id).ToListAsync();
    Assert.Equal(letters.Select(x => (x.Id, x.AcceptanceDecisionId, x.ProposalId, x.QuotationVersionId, x.SignatureSpecimenId, x.FirmSealSpecimenId, x.CommercialAcceptedAt, x.Sha256Hex)),
      retained.Select(x => (x.Id, x.AcceptanceDecisionId, x.ProposalId, x.QuotationVersionId, x.SignatureSpecimenId, x.FirmSealSpecimenId, x.CommercialAcceptedAt, x.Sha256Hex)));
    Assert.Equal(letters[0].Bytes, retained[0].Bytes);
  }

  // Explicit positive-fixture step after accepted bundle inputs, never part of generic Partner clearance.
  internal static async Task ReviewSupportedBundleApplicabilityAsync(ITestPostgresDatabase pg, World w)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var generation = await db.ClientSafetyStates.AsNoTracking().Where(x => x.Id == w.ClientId && x.FirmId == w.FirmId)
      .Select(x => x.InputGeneration).SingleAsync();
    Assert.True(generation > w.InputGeneration);
    var exclusions = await db.AuditProcedures.AsNoTracking().Where(x => x.EngagementId == w.EngagementId && x.Id != w.ProcedureId)
      .OrderBy(x => x.SourceProcedureId).ToListAsync();
    Assert.Equal(164, exclusions.Count);
    foreach (var procedure in exclusions)
    {
      Assert.Equal(AuditApplicabilityStatuses.NotApplicableApproved, procedure.ApplicabilityStatus);
      var proposal = await AuditProgramService.DecideApplicabilityAsync(db, w.A("senior", "Senior"),
        new(procedure.Id, AuditApplicabilityActions.ProposeNotApplicable,
          $"Synthetic revenue cut-off fixture retains its excluded work area after bundle inputs: {procedure.SourceProcedureId}.",
          procedure.CurrentApplicabilityRevision, procedure.CurrentDefinitionRevision, Guid.NewGuid()));
      Assert.True(proposal.Succeeded, proposal.Message);
      var review = await AuditProgramService.DecideApplicabilityAsync(db, w.A("partner", "Partner"),
        new(procedure.Id, AuditApplicabilityActions.ApproveNotApplicable,
          "Synthetic independent review of unchanged fixture exclusions against accepted bundle inputs.",
          proposal.Value!.ApplicabilityRevision, procedure.CurrentDefinitionRevision, Guid.NewGuid(), proposal.Value.DecisionId));
      Assert.True(review.Succeeded, review.Message);
    }
    Assert.Equal(generation, await db.ClientSafetyStates.AsNoTracking().Where(x => x.Id == w.ClientId && x.FirmId == w.FirmId)
      .Select(x => x.InputGeneration).SingleAsync());
  }

  private static async Task EstablishSupportedDeliverablesPlanningAsync(ITestPostgresDatabase pg, SupportedAuditCompletionWorld world,
    ActorContext preparer, ActorContext reviewer)
  {
    var accounting = await SupportedAuditCompletionWorldBuilder.ProvisionFirmRoleAsync(pg,
      (world.Primary.FirmId, world.Administrator), "AccountingPreparer");
    Guid datasetId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var period = await ClientAccountingService.CreatePeriodAsync(db, accounting.Actor,
        new(world.Primary.ClientId, "DEL-2026", new(2026, 1, 1), new(2026, 12, 31), "STATUTORY", "QAR"));
      Assert.True(period.Succeeded, period.Message);
      var imported = await TrialBalanceImportService.ImportAsync(db, accounting.Actor, world.Primary.ClientId,
        world.Primary.EngagementId, "AccountCode,AccountName,NetClosingBalance,Currency,Entity,MappingCode\n1000,Cash,100,QAR,TEST,CASH\n4000,Revenue,-100,QAR,TEST,REVENUE\n",
        new(period.Value, null, "STATUTORY"));
      Assert.True(imported.Succeeded, imported.Message);
      datasetId = imported.Value;
    }
    var factory = new OperationContextFactory(new DeliverablesDbContextFactory(pg.Options));
    var store = new PostgresOperationStore(factory);
    var options = new WorkerOptions(world.Primary.FirmId, "Test");
    var handler = new TrialBalanceValidationHandler();
    var worker = new NativeSourceWorker(new OperationDispatcher(store,
      new DurableOperationRegistry([handler], options), options),
      [new TrialBalanceDiscovery(factory, store, handler, options)], NullLogger<NativeSourceWorker>.Instance);
    Assert.True(await worker.ProcessNextAsync());
    await using var reviewDb = new AuditSphereDbContext(pg.Options);
    var accepted = await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(reviewDb, reviewer,
      new(world.Primary.ClientId, world.Primary.EngagementId, AccountingSourceKinds.TrialBalance, datasetId, null,
        "Synthetic independently reviewed balanced trial balance", Reviewed: true));
    Assert.True(accepted.Succeeded, accepted.Message);
    var taxonomy = await ClientAccountingService.CreateTaxonomyVersionAsync(reviewDb, world.Administrator,
      "TEST-IFRS", "IFRS", "Synthetic approved FSLI taxonomy", new(2026, 1, 1));
    Assert.True(taxonomy.Succeeded, taxonomy.Message);
    var nodes = await ClientAccountingService.AddTaxonomyNodesAsync(reviewDb, world.Administrator, taxonomy.Value,
    [
      new("CASH", "Cash", "ASSETS", "SIGNED", "DEBIT", "BALANCE_SHEET", true, "ALL"),
      new("REVENUE", "Revenue", "INCOME", "SIGNED", "CREDIT", "INCOME_STATEMENT", true, "ALL"),
      new("INV", "Inventory", "ASSETS", "SIGNED", "DEBIT", "Inventory", true, "ALL"),
      new("PPE", "Property and equipment", "ASSETS", "SIGNED", "DEBIT", "PPE", true, "ALL"),
      new("BORROWINGS", "Borrowings", "LIABILITIES", "SIGNED", "CREDIT", "BORROWINGS", true, "ALL"),
      new("SHARE_CAPITAL", "Share capital", "EQUITY", "SIGNED", "CREDIT", "SHARE_CAPITAL", true, "ALL"),
      new("REVALUATION", "Revaluation surplus", "OCI", "SIGNED", "CREDIT", "REVALUATION", true, "ALL")
    ]);
    Assert.True(nodes.Succeeded, nodes.Message);
    Assert.True((await ClientAccountingService.PublishTaxonomyVersionAsync(reviewDb, reviewer, taxonomy.Value)).Succeeded);
    var mapping = await FinancialStatementService.CreateMappingVersionAsync(reviewDb, preparer,
      new(datasetId, "TEST-IFRS", "2026-01-01", "2026-12-31",
      [new("1000", "CASH", "ASSETS", 1m, "Mapped"), new("4000", "REVENUE", "INCOME", 1m, "Mapped")]));
    Assert.True(mapping.Succeeded, mapping.Message);
    var mappingReview = await MappingApprovalWorkspace.GetAsync(reviewDb, reviewer, mapping.Value);
    Assert.True(mappingReview.Succeeded, mappingReview.Message);
    Assert.True((await FinancialStatementService.ApproveMappingAsync(reviewDb, reviewer, mapping.Value, 1,
      reviewRevision: mappingReview.Value!.Revision, reviewed: true)).Succeeded);
    var materiality = await MaterialityEngineService.CalculateAsync(reviewDb, preparer,
      new(world.Primary.EngagementId, MaterialityBenchmarks.Revenue, null, 1m, 75m, 5m, "Revenue-driven trading entity"));
    Assert.True(materiality.Succeeded, materiality.Message);
    Assert.True((await AuditPlanningService.ApproveMaterialityAssessmentAsync(reviewDb, reviewer, materiality.Value!.AssessmentId)).Succeeded);
  }

  private sealed class DeliverablesDbContextFactory(DbContextOptions<AuditSphereDbContext> options) : IDbContextFactory<AuditSphereDbContext>
  {
    public AuditSphereDbContext CreateDbContext() => new(options);
    public Task<AuditSphereDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AuditSphereDbContext(options));
  }
}
