using System.Data;
using System.Data.Common;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class PbcTransferTests
{
  [Fact]
  public async Task WorkflowApplicability_BatchesLargePlansWithoutCrossClientSourceCacheReuse()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var transfer = await CreateTransferWorkerAsync(pg, scripted: null);
    PbcSeed.StagedUpload? secondUpload = null;
    try
    {
      Assert.True(await transfer.Worker.ProcessNextAsync());
      var a = transfer.Fixture;
      var b = a with { ClientId = Guid.NewGuid(), EngagementId = Guid.NewGuid() };
      var maker = PbcSeed.Actor(a.Admin, "Administrator");
      var checker = PbcSeed.Actor(a.Reviewer, "Partner");
      await using (var db = new AuditSphereDbContext(pg.Options))
      {
        db.PracticeClients.Add(new PracticeClient { Id = b.ClientId, FirmId = a.FirmId, LegalName = "Synthetic second cache scope", CreatedAt = DateTimeOffset.UtcNow });
        db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement { Id = b.EngagementId, FirmId = a.FirmId,
          PracticeClientId = b.ClientId, Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow });
        db.ClientSafetyStates.Add(new AuditSphereOps.Domain.Completion.ClientSafetyState { Id = b.ClientId, FirmId = a.FirmId });
        db.RoleGrants.AddRange(PbcSeed.Grant(a.FirmId, a.Staff, "Staff", b.ClientId, b.EngagementId),
          PbcSeed.Grant(a.FirmId, a.Reviewer, "Reviewer", b.ClientId, b.EngagementId),
          PbcSeed.Grant(a.FirmId, a.Client, "ClientUser", b.ClientId, b.EngagementId), PbcSeed.Grant(a.FirmId, a.Reviewer, "Partner"));
        await db.SaveChangesAsync();
      }
      var secondRequest = await PbcSeed.CreateSentAcknowledgedRequestAsync(pg, b, PbcSeed.Actor(a.Staff, "Staff"), PbcSeed.Actor(a.Client, "ClientUser"));
      secondUpload = await PbcSeed.StageUploadAsync(pg, b, PbcSeed.Actor(a.Client, "ClientUser"), secondRequest, "synthetic second scoped source"u8.ToArray());
      var factory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options));
      var store = new PostgresOperationStore(factory);
      var handler = new PbcDocumentTransferHandler(factory, new SimulationPbcProviderSink(transfer.ProviderRoot));
      await using (var db = new AuditSphereDbContext(pg.Options))
        Assert.True((await PbcService.CompleteUploadAsync(db, PbcSeed.Actor(a.Staff, "Staff"),
          new CompletePbcUploadRequest(secondUpload.UploadIntentId, secondUpload.DeclaredSha256Hex), store, handler)).Succeeded);
      Assert.True(await transfer.Worker.ProcessNextAsync());

      async Task<(Guid ReceiptId, Guid[] Tasks)> PlanAsync(PbcSeed.Fixture f, Guid requestId, PbcSeed.StagedUpload upload, int count)
      {
        await using var db = new AuditSphereDbContext(pg.Options);
        var engagement = await db.Engagements.SingleAsync(x => x.Id == f.EngagementId);
        engagement.ServiceRoute = "FinancialStatementAudit"; engagement.ServiceProfileId = "SyntheticBatch";
        engagement.PeriodStart = "2026-01-01"; engagement.PeriodEnd = "2026-12-31"; await db.SaveChangesAsync();
        var reviewer = PbcSeed.Actor(a.Reviewer, "Reviewer");
        Assert.True((await PbcService.ChangeStateAsync(db, reviewer, new(requestId, PbcStates.UnderReview, 5))).Succeeded);
        var intent = await db.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.Id == upload.UploadIntentId);
        Assert.True((await PbcService.ChangeStateAsync(db, reviewer, new(requestId, PbcStates.Accepted, 6,
          ReviewedUploadIntentId: intent.Id, ReviewedUploadRevision: intent.Revision, ReviewedContentSha256Hex: upload.DeclaredSha256Hex))).Succeeded);
        var receipt = await db.PbcAcceptanceReceipts.AsNoTracking().SingleAsync(x => x.PbcRequestId == requestId);
        var steps = Enumerable.Range(1, count).Select(i => new WorkflowTemplateStepDraft($"STEP-{i}", WorkflowTaskPhases.AuditPlanning,
          $"Synthetic conditional {i}", "Exact source review.", null, null, null, null, true, i,
          ApplicabilityCondition: "Synthetic scoped condition")).ToArray();
        var draft = await WorkflowTemplateService.CreateDraftAsync(db, maker, new(null, "Synthetic bounded scope", engagement.ServiceRoute, null, steps));
        Assert.True(draft.Succeeded, draft.Message);
        var template = await db.WorkflowTemplateRevisions.SingleAsync(x => x.Id == draft.Value);
        Assert.True((await WorkflowTemplateService.ReviewAsync(db, checker, template.TemplateId, template.Id, template.Revision, "Synthetic independent review.")).Succeeded);
        await db.Entry(template).ReloadAsync();
        Assert.True((await WorkflowTemplateService.PublishAsync(db, checker, template.TemplateId, template.Id, template.Revision, "Synthetic publication.")).Succeeded);
        var preview = await EngagementWorkflowPlanService.PreviewAsync(db, maker, f.EngagementId, new(template.Id));
        Assert.True(preview.Succeeded, preview.Message);
        var plan = await EngagementWorkflowPlanService.ExecuteAsync(db, maker, f.EngagementId, new(template.Id, preview.Value!.PreviewDigest, Guid.NewGuid()));
        Assert.True(plan.Succeeded, plan.Message); return (receipt.Id, plan.Value!.TaskIds.ToArray());
      }
      // Published plans intentionally stop at 100 steps. A later canonical manual task crosses the read-page boundary.
      var planA = await PlanAsync(a, transfer.RequestId, transfer.Staged, 100);
      await using (var db = new AuditSphereDbContext(pg.Options))
      {
        var extra = await EngagementWorkflowTaskService.CreateAsync(db, maker, a.EngagementId,
          new("Synthetic extra required task", WorkflowTaskPhases.AuditPlanning, 101, RequiredTask: true, IdempotencyKey: Guid.NewGuid()));
        Assert.True(extra.Succeeded, extra.Message);
      }
      var planB = await PlanAsync(b, secondRequest, secondUpload, 1);
      await using var read = new AuditSphereDbContext(pg.Options);
      var bindingsA = await read.WorkflowTaskBindings.AsNoTracking().Where(x => x.EngagementId == a.EngagementId).OrderBy(x => x.Ordinal).ToArrayAsync();
      var bindingB = await read.WorkflowTaskBindings.AsNoTracking().SingleAsync(x => x.EngagementId == b.EngagementId);
      var page = bindingsA.Take(99).Append(bindingB).ToArray();
      var inputs = page.Select(x => new WorkflowTaskApplicabilityBasisInput(x, x.ClientId == a.ClientId ? planA.ReceiptId : planB.ReceiptId)).ToArray();
      IReadOnlyDictionary<Guid, CommandResult<WorkflowTaskApplicabilityBasis>> initial;
      await using (var snapshot = await read.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead))
      {
        initial = await WorkflowTaskApplicabilityBasisQuery.GetManyAsync(read, a.FirmId, inputs, default);
        Assert.Equal(100, initial.Count); Assert.All(initial.Values, x => Assert.True(x.Succeeded, x.Message));
        var swapped = await WorkflowTaskApplicabilityBasisQuery.GetManyAsync(read, a.FirmId, [new(bindingsA[0], planB.ReceiptId)], default);
        Assert.False(swapped[bindingsA[0].Id].Succeeded);
        await Assert.ThrowsAsync<InvalidOperationException>(() => WorkflowTaskApplicabilityBasisQuery.GetManyAsync(read, a.FirmId,
          inputs.Append(new(bindingsA[99], planA.ReceiptId)).ToArray(), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => WorkflowTaskApplicabilityBasisQuery.GetManyAsync(read, a.FirmId, [inputs[0], inputs[0]], default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => WorkflowTaskApplicabilityQuery.EffectiveManyAsync(read, a.FirmId, bindingsA, default));
        await snapshot.CommitAsync();
      }
      foreach (var (binding, receiptId) in new[] { (bindingsA[0], planA.ReceiptId), (bindingsA[99], planA.ReceiptId), (bindingB, planB.ReceiptId) })
      {
        await using var db = new AuditSphereDbContext(pg.Options);
        var basis = await WorkflowTaskApplicabilityBasisQuery.GetAsync(db, binding, receiptId, default); Assert.True(basis.Succeeded, basis.Message);
        var proposal = await PracticeTimeService.ProposeWorkflowApplicabilityAsync(db, maker, binding.EngagementId, binding.WorkTaskId,
          new(receiptId, binding.Revision, null, basis.Value!.Hash, "Synthetic exact scope proposal.", Guid.NewGuid()));
        Assert.True(proposal.Succeeded, proposal.Message);
        var approved = await PracticeTimeService.DecideWorkflowApplicabilityAsync(db, checker, binding.EngagementId, binding.WorkTaskId,
          new(proposal.Value!.ProposalId, null, WorkflowTaskApplicabilityDecisions.Approve, binding.Revision, basis.Value.Hash, "Synthetic independent scoped decision.", Guid.NewGuid()));
        Assert.True(approved.Succeeded, approved.Message);
      }
      var progressA = await EngagementProgressQuery.GetAsync(read, maker, a.EngagementId);
      Assert.True(progressA.Succeeded, progressA.Message); Assert.Equal(101, progressA.Value!.Steps.Count);
      Assert.Equal(2, progressA.Value.NotApplicable); Assert.Equal(99, progressA.Value.ApplicableRequired);
      Assert.Equal(1, (await EngagementProgressQuery.GetAsync(read, maker, b.EngagementId)).Value!.NotApplicable);
      await read.ClientSafetyStates.Where(x => x.Id == a.ClientId && x.FirmId == a.FirmId)
        .ExecuteUpdateAsync(x => x.SetProperty(y => y.InputGeneration, y => y.InputGeneration + 1));
      await using (var snapshot = await read.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead))
      {
        var changed = await WorkflowTaskApplicabilityBasisQuery.GetManyAsync(read, a.FirmId, inputs, default);
        Assert.NotEqual(initial[bindingsA[0].Id].Value!.Hash, changed[bindingsA[0].Id].Value!.Hash);
        Assert.Equal(initial[bindingB.Id].Value!.Hash, changed[bindingB.Id].Value!.Hash);
        var effective = await WorkflowTaskApplicabilityQuery.EffectiveManyAsync(read, a.FirmId, page, default);
        Assert.False(effective[bindingsA[0].Id]); Assert.True(effective[bindingB.Id]);
        await snapshot.CommitAsync();
      }
      Assert.Equal(0, (await EngagementProgressQuery.GetAsync(read, maker, a.EngagementId)).Value!.NotApplicable);
      Assert.Equal(1, (await EngagementProgressQuery.GetAsync(read, maker, b.EngagementId)).Value!.NotApplicable);
    }
    finally { PbcSeed.DeleteDirectory(transfer.Staged.StagingRoot); if (secondUpload is not null) PbcSeed.DeleteDirectory(secondUpload.StagingRoot); }
  }

  [Fact]
  public async Task WorkflowApplicability_BindsIndependentDecisionsToCurrentSourceAndRetainsStaleWithdrawal()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var transfer = await CreateTransferWorkerAsync(pg, scripted: null);
    try
    {
      Assert.True(await transfer.Worker.ProcessNextAsync());
      var f = transfer.Fixture;
      var maker = PbcSeed.Actor(f.Admin, "Administrator");
      var checker = PbcSeed.Actor(f.Reviewer, "Partner");
      Guid taskId;
      Guid successorTaskId;
      Guid receiptId;
      await using (var db = new AuditSphereDbContext(pg.Options))
      {
        var engagement = await db.Engagements.SingleAsync(x => x.Id == f.EngagementId);
        engagement.ServiceRoute = "FinancialStatementAudit";
        engagement.ServiceProfileId = "SyntheticApplicability";
        engagement.PeriodStart = "2026-01-01";
        engagement.PeriodEnd = "2026-12-31";
        db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Reviewer, "Partner"));
        await db.SaveChangesAsync();
        var reviewer = PbcSeed.Actor(f.Reviewer, "Reviewer");
        Assert.True((await PbcService.ChangeStateAsync(db, reviewer,
          new(transfer.RequestId, PbcStates.UnderReview, 5))).Succeeded);
        var upload = await db.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.Id == transfer.Staged.UploadIntentId);
        var accepted = await PbcService.ChangeStateAsync(db, reviewer,
          new(transfer.RequestId, PbcStates.Accepted, 6, ReviewedUploadIntentId: upload.Id,
            ReviewedUploadRevision: upload.Revision, ReviewedContentSha256Hex: transfer.Staged.DeclaredSha256Hex));
        Assert.True(accepted.Succeeded, accepted.Message);
        receiptId = (await db.PbcAcceptanceReceipts.AsNoTracking().SingleAsync(x => x.PbcRequestId == transfer.RequestId)).Id;
        var draft = await WorkflowTemplateService.CreateDraftAsync(db, maker,
          new(null, "Synthetic conditional task", engagement.ServiceRoute, null,
            [new("CONDITIONAL", WorkflowTaskPhases.AuditPlanning, "Review inventory evidence", "Review exact source evidence.",
              null, null, null, null, true, 1, ApplicabilityCondition: "Inventory exists in this engagement period") ]));
        Assert.True(draft.Succeeded, draft.Message);
        var template = await db.WorkflowTemplateRevisions.SingleAsync(x => x.Id == draft.Value);
        Assert.True((await WorkflowTemplateService.ReviewAsync(db, checker, template.TemplateId, template.Id,
          template.Revision, "Synthetic independent template review.")).Succeeded);
        await db.Entry(template).ReloadAsync();
        Assert.True((await WorkflowTemplateService.PublishAsync(db, checker, template.TemplateId, template.Id,
          template.Revision, "Synthetic reviewed template publication.")).Succeeded);
        var preview = await EngagementWorkflowPlanService.PreviewAsync(db, maker, f.EngagementId, new(template.Id));
        Assert.True(preview.Succeeded, preview.Message);
        var plan = await EngagementWorkflowPlanService.ExecuteAsync(db, maker, f.EngagementId,
          new(template.Id, preview.Value!.PreviewDigest, Guid.NewGuid()));
        Assert.True(plan.Succeeded, plan.Message);
        taskId = Assert.Single(plan.Value!.TaskIds);
        var successorTask = await EngagementWorkflowTaskService.CreateAsync(db, maker, f.EngagementId,
          new("Synthetic dependent task", WorkflowTaskPhases.AuditPlanning, 2, RequiredTask: true, IdempotencyKey: Guid.NewGuid()));
        Assert.True(successorTask.Succeeded, successorTask.Message);
        successorTaskId = successorTask.Value;
        var dependency = await PracticeTimeService.ChangeWorkflowDependencyAsync(db, maker, f.EngagementId, successorTaskId,
          new(taskId, true, 1, 1, "Synthetic task-completed prerequisite remains strict.", Guid.NewGuid()));
        Assert.True(dependency.Succeeded, dependency.Message);
      }

      async Task<WorkflowTaskApplicabilityBasis> BasisAsync()
      {
        await using var read = new AuditSphereDbContext(pg.Options);
        await using var snapshot = await read.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var binding = await read.WorkflowTaskBindings.AsNoTracking().SingleAsync(x => x.WorkTaskId == taskId);
        var result = await WorkflowTaskApplicabilityBasisQuery.GetAsync(read, binding, receiptId, default);
        Assert.True(result.Succeeded, result.Message);
        return result.Value!;
      }
      async Task<bool> EffectiveAsync()
      {
        await using var read = new AuditSphereDbContext(pg.Options);
        await using var snapshot = await read.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var binding = await read.WorkflowTaskBindings.AsNoTracking().SingleAsync(x => x.WorkTaskId == taskId);
        return (await WorkflowTaskApplicabilityQuery.EffectiveManyAsync(read, f.FirmId, [binding], default))[binding.Id];
      }
      var basis = await BasisAsync();
      EngagementProgressView baselineProgress;
      await using (var progressRead = new AuditSphereDbContext(pg.Options))
      {
        var baseline = await EngagementProgressQuery.GetAsync(progressRead, maker, f.EngagementId);
        Assert.True(baseline.Succeeded, baseline.Message);
        baselineProgress = baseline.Value!;
        Assert.Equal(EngagementProgressStepStates.Pending, Assert.Single(baselineProgress.Steps, x => x.TaskId == taskId).State);
        Assert.Equal(EngagementProgressStepStates.Blocked, Assert.Single(baselineProgress.Steps, x => x.TaskId == successorTaskId).State);
      }
      await using (var previewRead = new AuditSphereDbContext(pg.Options))
      {
        var preview = await WorkflowTaskApplicabilityQuery.GetAsync(previewRead, maker, f.EngagementId, taskId, transfer.RequestId);
        Assert.True(preview.Succeeded, preview.Message);
        Assert.True(preview.Value!.CanPropose);
        Assert.Equal(basis.Hash, preview.Value.Preview!.BasisHash);
        Assert.Equal(receiptId, preview.Value.Preview.SourceReceiptId);
        var staffRead = await WorkflowTaskApplicabilityQuery.GetAsync(previewRead, PbcSeed.Actor(f.Staff, "Staff"),
          f.EngagementId, taskId, transfer.RequestId);
        Assert.True(staffRead.Succeeded, staffRead.Message);
        Assert.False(staffRead.Value!.CanPropose);
        Assert.Equal(ErrorCodes.ScopeDenied, (await WorkflowTaskApplicabilityQuery.GetAsync(previewRead,
          PbcSeed.Actor(f.Client, "ClientUser"), f.EngagementId, taskId, transfer.RequestId)).ErrorCode);
        await using var callerTransaction = await previewRead.Database.BeginTransactionAsync();
        Assert.Equal(ErrorCodes.GateBlocked, (await WorkflowTaskApplicabilityQuery.GetAsync(previewRead, maker,
          f.EngagementId, taskId, transfer.RequestId)).ErrorCode);
        Assert.Same(callerTransaction, previewRead.Database.CurrentTransaction);
      }
      var propose = new WorkflowApplicabilityProposalRequest(receiptId, basis.TaskRevision, null, basis.Hash,
        "Synthetic maker statement tied to the exact independently accepted file.", Guid.NewGuid());
      await using var commands = new AuditSphereDbContext(pg.Options);
      Assert.Equal(ErrorCodes.ScopeDenied, (await PracticeTimeService.ProposeWorkflowApplicabilityAsync(commands,
        PbcSeed.Actor(f.Staff, "Partner"), f.EngagementId, taskId, propose)).ErrorCode);
      var proposal = await PracticeTimeService.ProposeWorkflowApplicabilityAsync(commands, maker, f.EngagementId, taskId, propose);
      Assert.True(proposal.Succeeded, proposal.Message);
      Assert.Equal(proposal.Value, (await PracticeTimeService.ProposeWorkflowApplicabilityAsync(commands, maker,
        f.EngagementId, taskId, propose)).Value);
      Assert.Equal(ErrorCodes.IdempotencyConflict, (await PracticeTimeService.ProposeWorkflowApplicabilityAsync(commands, maker,
        f.EngagementId, taskId, propose with { Reason = "Different reviewed statement" })).ErrorCode);
      Assert.Equal(proposal.Value, (await WorkflowTaskApplicabilityQuery.ReceiptAsync(commands, maker,
        f.EngagementId, taskId, propose.IdempotencyKey)).Value);
      Assert.Equal(ErrorCodes.GateBlocked, (await WorkflowTaskApplicabilityQuery.ReceiptAsync(commands, checker,
        f.EngagementId, taskId, propose.IdempotencyKey)).ErrorCode);
      var makerView = await WorkflowTaskApplicabilityQuery.GetAsync(commands, maker, f.EngagementId, taskId);
      var checkerView = await WorkflowTaskApplicabilityQuery.GetAsync(commands, checker, f.EngagementId, taskId);
      Assert.False(makerView.Value!.CanApprove);
      Assert.True(checkerView.Value!.CanApprove);
      Assert.False(await EffectiveAsync()); // Proposal alone cannot exclude an operational task.
      var approve = new WorkflowApplicabilityDecisionRequest(proposal.Value!.ProposalId, null,
        WorkflowTaskApplicabilityDecisions.Approve, basis.TaskRevision, basis.Hash, "Synthetic independent exact-basis review.", Guid.NewGuid());
      Assert.Equal(ErrorCodes.ScopeDenied, (await PracticeTimeService.DecideWorkflowApplicabilityAsync(commands, maker,
        f.EngagementId, taskId, approve)).ErrorCode);
      Assert.Equal(ErrorCodes.GenerationStale, (await PracticeTimeService.ProposeWorkflowApplicabilityAsync(commands,
        new ActorContext(f.Admin.Id, f.FirmId, maker.SessionEpoch + 1, ["Administrator"]), f.EngagementId, taskId,
        propose with { IdempotencyKey = Guid.NewGuid() })).ErrorCode);
      CommandResult<WorkflowApplicabilityReceipt> approval;
      // Both exact independent approval requests must actually wait on the firm publication guard.
      // Releasing it leaves one saved head; the other must observe that committed head and refuse.
      using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
      {
        await using var holder = new AuditSphereDbContext(pg.Options);
        await using var held = await holder.Database.BeginTransactionAsync(timeout.Token);
        await holder.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={f.FirmId} FOR UPDATE")
          .AsNoTracking().SingleAsync(timeout.Token);
        var holderPid = await holder.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync(timeout.Token);
        var nameA = "task03-approve-a-" + Guid.NewGuid().ToString("N");
        var nameB = "task03-approve-b-" + Guid.NewGuid().ToString("N");
        var connectionA = new NpgsqlConnectionStringBuilder(pg.ConnectionString) { ApplicationName = nameA };
        var connectionB = new NpgsqlConnectionStringBuilder(pg.ConnectionString) { ApplicationName = nameB };
        await using var dbA = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>().UseNpgsql(connectionA.ConnectionString).Options);
        await using var dbB = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>().UseNpgsql(connectionB.ConnectionString).Options);
        var approveB = approve with { IdempotencyKey = Guid.NewGuid() };
        var taskA = PracticeTimeService.DecideWorkflowApplicabilityAsync(dbA, checker, f.EngagementId, taskId, approve, timeout.Token);
        var taskB = PracticeTimeService.DecideWorkflowApplicabilityAsync(dbB, checker, f.EngagementId, taskId, approveB, timeout.Token);
        try
        {
          var witnessA = await WaitForApplicabilityPublicationLockAsync(commands, nameA, holderPid, timeout.Token);
          var witnessB = await WaitForApplicabilityPublicationLockAsync(commands, nameB, holderPid, timeout.Token);
          Console.WriteLine("TASK03_LOCK_WITNESS_A " + witnessA);
          Console.WriteLine("TASK03_LOCK_WITNESS_B " + witnessB);
        }
        finally { await held.CommitAsync(CancellationToken.None); }
        var decisions = await Task.WhenAll(taskA, taskB);
        approval = Assert.Single(decisions, x => x.Succeeded);
        Assert.Equal(ErrorCodes.StaleRevision, Assert.Single(decisions, x => !x.Succeeded).ErrorCode);
        if (!decisions[0].Succeeded) approve = approveB;
      }
      Assert.True(approval.Succeeded, approval.Message);
      Assert.Equal(approval.Value, (await PracticeTimeService.DecideWorkflowApplicabilityAsync(commands, checker,
        f.EngagementId, taskId, approve)).Value);
      Assert.Equal(1, await commands.WorkflowTaskApplicabilityDecisions.CountAsync());
      Assert.True(await EffectiveAsync());
      var approvedProgress = await EngagementProgressQuery.GetAsync(commands, maker, f.EngagementId);
      Assert.True(approvedProgress.Succeeded, approvedProgress.Message);
      Assert.Equal(baselineProgress.ApplicableRequired - 1, approvedProgress.Value!.ApplicableRequired);
      Assert.Equal(baselineProgress.NotApplicable + 1, approvedProgress.Value.NotApplicable);
      Assert.Equal(EngagementProgressStepStates.NotApplicable, Assert.Single(approvedProgress.Value.Steps, x => x.TaskId == taskId).State);
      Assert.Equal(EngagementProgressStepStates.Blocked, Assert.Single(approvedProgress.Value.Steps, x => x.TaskId == successorTaskId).State);
      Assert.Equal(ErrorCodes.GateBlocked, (await EngagementWorkflowTaskService.CompleteAsync(commands, maker, successorTaskId, 2)).ErrorCode);
      Assert.Equal(approval.Value, (await WorkflowTaskApplicabilityQuery.ReceiptAsync(commands, checker,
        f.EngagementId, taskId, approve.IdempotencyKey)).Value);
      Assert.Equal(basis.TaskRevision, await commands.WorkflowTaskBindings.Where(x => x.WorkTaskId == taskId).Select(x => x.Revision).SingleAsync());
      Assert.NotEqual(PracticeTimeStates.TaskCompleted, (await commands.WorkTasks.AsNoTracking().SingleAsync(x => x.Id == taskId)).Status);

      await commands.ClientSafetyStates.Where(x => x.FirmId == f.FirmId && x.Id == f.ClientId)
        .ExecuteUpdateAsync(x => x.SetProperty(y => y.InputGeneration, y => y.InputGeneration + 1));
      Assert.False(await EffectiveAsync()); // Changed current source context restores applicability.
      var restoredProgress = await EngagementProgressQuery.GetAsync(commands, maker, f.EngagementId);
      Assert.True(restoredProgress.Succeeded, restoredProgress.Message);
      Assert.Equal(baselineProgress.ApplicableRequired, restoredProgress.Value!.ApplicableRequired);
      Assert.Equal(baselineProgress.NotApplicable, restoredProgress.Value.NotApplicable);
      var changedBasis = await BasisAsync();
      Assert.NotEqual(basis.Hash, changedBasis.Hash);
      var successor = await PracticeTimeService.ProposeWorkflowApplicabilityAsync(commands, maker, f.EngagementId, taskId,
        propose with { ExpectedProposalId = proposal.Value.ProposalId, BasisHash = changedBasis.Hash, IdempotencyKey = Guid.NewGuid() });
      Assert.True(successor.Succeeded, successor.Message);
      await commands.ClientSafetyStates.Where(x => x.FirmId == f.FirmId && x.Id == f.ClientId)
        .ExecuteUpdateAsync(x => x.SetProperty(y => y.InputGeneration, y => y.InputGeneration + 1));
      var staleApproval = approve with { ProposalId = successor.Value!.ProposalId, BasisHash = changedBasis.Hash, IdempotencyKey = Guid.NewGuid() };
      Assert.Equal(ErrorCodes.StaleRevision, (await PracticeTimeService.DecideWorkflowApplicabilityAsync(commands, checker,
        f.EngagementId, taskId, staleApproval)).ErrorCode);
      await commands.Engagements.Where(x => x.Id == f.EngagementId).ExecuteUpdateAsync(x => x.SetProperty(y => y.ProfessionalWorkBlocked, true));
      Assert.True((await PracticeTimeService.DecideWorkflowApplicabilityAsync(commands, checker, f.EngagementId, taskId,
        staleApproval with { Kind = WorkflowTaskApplicabilityDecisions.Return, Reason = "Return stale synthetic evidence.", IdempotencyKey = Guid.NewGuid() })).Succeeded);
      var revoked = await PracticeTimeService.DecideWorkflowApplicabilityAsync(commands, checker, f.EngagementId, taskId,
        approve with { Kind = WorkflowTaskApplicabilityDecisions.Revoke, ExpectedDecisionId = approval.Value!.DecisionId,
          Reason = "Revoke the earlier synthetic approval after source change.", IdempotencyKey = Guid.NewGuid() });
      Assert.True(revoked.Succeeded, revoked.Message);
      Assert.Equal(3, await commands.WorkflowTaskApplicabilityDecisions.CountAsync());
      var update = await Assert.ThrowsAsync<PostgresException>(() => commands.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE workflow_task_applicability_proposals SET reason='rewrite' WHERE id={proposal.Value.ProposalId}"));
      Assert.Equal(PostgresErrorCodes.RaiseException, update.SqlState);
      var truncate = await Assert.ThrowsAsync<PostgresException>(() => commands.Database.ExecuteSqlRawAsync(
        "TRUNCATE workflow_task_applicability_decisions, workflow_task_applicability_proposals"));
      Assert.Equal(PostgresErrorCodes.RaiseException, truncate.SqlState);
      Assert.Equal(2, await commands.WorkflowTaskApplicabilityProposals.CountAsync());
      var heldView = await WorkflowTaskApplicabilityQuery.GetAsync(commands, checker, f.EngagementId, taskId,
        proposalId: proposal.Value.ProposalId);
      Assert.True(heldView.Succeeded, heldView.Message);
      Assert.False(heldView.Value!.CanApprove);
      Assert.False(heldView.Value.Proposal!.BasisCurrent);
      Assert.False(heldView.Value.Proposal.IsLatest);
      Assert.Equal(WorkflowTaskApplicabilityDecisions.Revoke, heldView.Value.Proposal.Decision!.Kind);
      Assert.Equal(proposal.Value, (await WorkflowTaskApplicabilityQuery.ReceiptAsync(commands, maker,
        f.EngagementId, taskId, propose.IdempotencyKey)).Value); // Historical recovery is not current applicability.
      var history = await WorkflowTaskApplicabilityQuery.HistoryAsync(commands, checker, f.EngagementId, taskId);
      Assert.True(history.Succeeded, history.Message);
      Assert.Equal(5, history.Value!.Items.Count);
      Assert.False(history.Value.HasMore);
      Assert.False(await EffectiveAsync());
      // Cross the documented 25-row history boundary using real successor proposals, not fabricated rows.
      await commands.Engagements.Where(x => x.Id == f.EngagementId).ExecuteUpdateAsync(x => x.SetProperty(y => y.ProfessionalWorkBlocked, false));
      var latestId = successor.Value!.ProposalId;
      var latestBasis = await BasisAsync();
      for (var index = 0; index < 21; index++)
      {
        var next = await PracticeTimeService.ProposeWorkflowApplicabilityAsync(commands, maker, f.EngagementId, taskId,
          propose with { ExpectedProposalId = latestId, BasisHash = latestBasis.Hash,
            Reason = "Synthetic retained successor " + index, IdempotencyKey = Guid.NewGuid() });
        Assert.True(next.Succeeded, next.Message);
        latestId = next.Value!.ProposalId;
      }
      var first = await WorkflowTaskApplicabilityQuery.HistoryAsync(commands, checker, f.EngagementId, taskId);
      Assert.True(first.Succeeded, first.Message);
      Assert.Equal(25, first.Value!.Items.Count);
      Assert.True(first.Value.HasMore);
      Assert.NotNull(first.Value.NextCursor);
      var second = await WorkflowTaskApplicabilityQuery.HistoryAsync(commands, checker, f.EngagementId, taskId, first.Value.NextCursor);
      Assert.True(second.Succeeded, second.Message);
      Assert.Single(second.Value!.Items);
      Assert.False(second.Value.HasMore);
      Assert.Equal(26, first.Value.Items.Concat(second.Value.Items).Select(x => (x.RecordType, x.Id)).Distinct().Count());
      var refresh = await WorkflowTaskApplicabilityQuery.HistoryAsync(commands, checker, f.EngagementId, taskId);
      Assert.Equal(first.Value.Items, refresh.Value!.Items);
      Assert.Equal(23, await commands.WorkflowTaskApplicabilityProposals.CountAsync());
      Assert.Equal(3, await commands.WorkflowTaskApplicabilityDecisions.CountAsync());
      // A task-only approval must never supply the linked professional procedure's independent N/A decision.
      var procedure = new AuditProcedure { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId,
        EngagementId = f.EngagementId, SourceProcedureId = "SYN-TASK-NA", Title = "Synthetic independent inventory procedure",
        Status = AuditProcedureStatuses.Planned, ApplicabilityStatus = AuditApplicabilityStatuses.Pending, CreatedAt = DateTimeOffset.UtcNow };
      commands.AuditProcedures.Add(procedure);
      await commands.SaveChangesAsync();
      var requirement = await PracticeTimeService.ChangeWorkflowEvidenceAsync(commands, maker, f.EngagementId, taskId,
        new(WorkflowEvidenceKinds.ReviewedProcedure, procedure.Id, true, "Retain the separate professional applicability decision.",
          latestBasis.TaskRevision, Guid.NewGuid()));
      Assert.True(requirement.Succeeded, requirement.Message);
      var professionalBasis = await BasisAsync();
      var taskOnlyProposal = await PracticeTimeService.ProposeWorkflowApplicabilityAsync(commands, maker, f.EngagementId, taskId,
        propose with { ExpectedTaskRevision = professionalBasis.TaskRevision, ExpectedProposalId = latestId,
          BasisHash = professionalBasis.Hash, Reason = "Synthetic conditional task proposal, with professional procedure still pending.", IdempotencyKey = Guid.NewGuid() });
      Assert.True(taskOnlyProposal.Succeeded, taskOnlyProposal.Message);
      var taskOnlyApproval = await PracticeTimeService.DecideWorkflowApplicabilityAsync(commands, checker, f.EngagementId, taskId,
        new(taskOnlyProposal.Value!.ProposalId, null, WorkflowTaskApplicabilityDecisions.Approve, professionalBasis.TaskRevision,
          professionalBasis.Hash, "Synthetic task review does not decide the linked professional procedure.", Guid.NewGuid()));
      Assert.True(taskOnlyApproval.Succeeded, taskOnlyApproval.Message);
      Assert.True(await EffectiveAsync());
      var professionallyBlocked = await EngagementProgressQuery.GetAsync(commands, maker, f.EngagementId);
      Assert.True(professionallyBlocked.Succeeded, professionallyBlocked.Message);
      Assert.Equal(EngagementProgressStepStates.Blocked, Assert.Single(professionallyBlocked.Value!.Steps, x => x.TaskId == taskId).State);
      Assert.Equal(baselineProgress.ApplicableRequired, professionallyBlocked.Value.ApplicableRequired);
      Assert.Equal(baselineProgress.NotApplicable, professionallyBlocked.Value.NotApplicable);
      Assert.Equal(AuditApplicabilityStatuses.Pending, (await commands.AuditProcedures.AsNoTracking().SingleAsync(x => x.Id == procedure.Id)).ApplicabilityStatus);
      Assert.Empty(await commands.AuditProcedureApplicabilityDecisions.ToListAsync());
      var revokeAfterSnapshot = new ApplicabilityReadCommitted(async () =>
      {
        await using var revoke = new AuditSphereDbContext(pg.Options);
        await revoke.RoleGrants.Where(x => x.FirmId == f.FirmId && x.UserId == f.Reviewer.Id && x.Role == "Partner")
          .ExecuteUpdateAsync(x => x.SetProperty(y => y.RevokedAt, DateTimeOffset.UtcNow));
      });
      var revokedOptions = new DbContextOptionsBuilder<AuditSphereDbContext>().UseNpgsql(pg.ConnectionString)
        .AddInterceptors(revokeAfterSnapshot).Options;
      await using var revokedRead = new AuditSphereDbContext(revokedOptions);
      var withdrawnRead = await WorkflowTaskApplicabilityQuery.GetAsync(revokedRead, checker, f.EngagementId, taskId);
      Assert.Equal(1, revokeAfterSnapshot.Commits);
      Assert.Equal(ErrorCodes.ScopeDenied, withdrawnRead.ErrorCode);
      Assert.Null(withdrawnRead.Value);
      Assert.Equal(24, await commands.WorkflowTaskApplicabilityProposals.CountAsync());
      Assert.Equal(4, await commands.WorkflowTaskApplicabilityDecisions.CountAsync());
    }
    finally { PbcSeed.DeleteDirectory(transfer.Staged.StagingRoot); }
  }

  private static async Task<string> WaitForApplicabilityPublicationLockAsync(AuditSphereDbContext observer,
    string applicationName, int rootPid, CancellationToken ct)
  {
    using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
    bounded.CancelAfter(TimeSpan.FromSeconds(10));
    // PostgreSQL may queue the second row-lock waiter behind the first waiter. Require the complete
    // blocker chain to reach the exact held firm guard, rather than assuming every blocker is direct.
    while (true)
    {
      var witness = await observer.Database.SqlQuery<string>($"""
        WITH RECURSIVE blockers(pid) AS (
          SELECT unnest(pg_blocking_pids(waiter.pid)) FROM pg_stat_activity waiter
          WHERE waiter.datname = current_database() AND waiter.application_name = {applicationName}
          UNION
          SELECT unnest(pg_blocking_pids(blockers.pid)) FROM blockers
        )
        SELECT json_build_object('waiterPid', waiter.pid, 'waitType', waiter.wait_event_type,
          'blockerChain', (SELECT json_agg(pid) FROM blockers), 'rootPid', {rootPid})::text AS "Value"
        FROM pg_stat_activity waiter
        WHERE waiter.datname = current_database() AND waiter.application_name = {applicationName}
          AND waiter.wait_event_type = 'Lock' AND waiter.query LIKE '%firm_safety_states%'
          AND EXISTS (SELECT 1 FROM blockers WHERE pid = {rootPid})
        """).SingleOrDefaultAsync(bounded.Token);
      if (witness is not null) return witness;
      await Task.Delay(TimeSpan.FromMilliseconds(20), bounded.Token);
    }
  }


  private sealed class ApplicabilityReadCommitted(Func<Task> afterCommit) : DbTransactionInterceptor
  {
    private int commits;
    public int Commits => commits;
    public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
      CancellationToken cancellationToken = default)
    {
      if (Interlocked.Increment(ref commits) == 1) await afterCommit();
    }
  }

}
