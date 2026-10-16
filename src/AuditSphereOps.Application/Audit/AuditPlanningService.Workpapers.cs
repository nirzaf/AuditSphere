using System.Diagnostics;
using AuditSphereOps.Application.Diagnostics;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;
namespace AuditSphereOps.Application.Audit;
public static partial class AuditPlanningService
{
    public static Task<CommandResult<WorkpaperDraftResult>> SaveWorkpaperDraftAsync(IAuditSphereDbContext db,ActorContext actor,SaveWorkpaperDraftRequest req,CancellationToken ct=default)=>WorkpaperCommandWorkspace.SaveLegacyAsync(db,actor,req,ct);
    public static Task<CommandResult<WorkpaperResult>> SubmitWorkpaperAsync(IAuditSphereDbContext db,ActorContext actor,SubmitWorkpaperRequest req,CancellationToken ct=default)=>WorkpaperCommandWorkspace.SubmitLegacyAsync(db,actor,req,ct);
    public static Task<CommandResult> DiscardWorkpaperDraftAsync(IAuditSphereDbContext db,ActorContext actor,Guid id,long revision,CancellationToken ct=default)=>WorkpaperCommandWorkspace.DiscardLegacyAsync(db,actor,id,revision,ct);

    internal static async Task<CommandResult<WorkpaperResult>> SubmitWorkpaperCoreAsync(
        IAuditSphereDbContext db,
        ActorContext actor,
        SubmitWorkpaperRequest req,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(req);
        using var activity = AuditDiagnostics.StartCommand("audit.workpaper.submit");
        activity?.SetTag("command", "workpaper.submit");

        if (Blank(req.Conclusion))
            return CommandResult<WorkpaperResult>.Fail(InvalidCode, "Conclusion is required.");
        if (Blank(req.WorkPerformed))
            return CommandResult<WorkpaperResult>.Fail(InvalidCode,
                "The work performed must be recorded before submission.");

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;

        // Snapshot read first, then the fixed lock order (engagement → target), then a locked
        // re-read so a concurrent edit cannot slip between the scope check and the freeze (§22.4).
        var snapshot = await db.Workpapers.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == req.WorkpaperId && x.FirmId == actor.FirmId, ct);
        if (snapshot is null)
            return CommandResult<WorkpaperResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

        var publication = await AuditPlanningCommandWorkspace.LockAsync(db, actor, snapshot.EngagementId, true, ct);
        if (!publication.Succeeded) return CommandResult<WorkpaperResult>.Fail(publication.ErrorCode!, publication.Message!);
        var scope = await LockedEngagementAsync(db, actor, snapshot.EngagementId, ct, snapshot.ClientId);
        if (scope.Denied is not null)
            return CommandResult<WorkpaperResult>.Fail(scope.Denied, scope.Message);

        var target = await db.Workpapers.FromSqlInterpolated($"""
            SELECT * FROM workpapers WHERE id = {req.WorkpaperId} AND firm_id = {actor.FirmId} FOR UPDATE
            """).AsNoTracking().SingleOrDefaultAsync(ct);
        if (target is null || target.ClientId != snapshot.ClientId ||
            target.EngagementId != snapshot.EngagementId)
            return CommandResult<WorkpaperResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

        if (target.Status != WorkpaperStatuses.Working)
            return CommandResult<WorkpaperResult>.Fail(ErrorCodes.ProtectedState,
                $"A {target.Status} workpaper cannot be submitted again; a corrected conclusion requires a new workpaper revision.");
        if (target.Revision != req.ExpectedRevision)
            return CommandResult<WorkpaperResult>.Fail(ErrorCodes.StaleRevision,
                "The workpaper changed while you were working; reload the current revision.");

        var generations = await ReadGenerationsAsync(db, target.FirmId, target.ClientId, ct);
        if (generations.InputGeneration < 1 || generations.PolicyGeneration < 1)
            return CommandResult<WorkpaperResult>.Fail(ErrorCodes.GateBlocked,"Workpaper safety state is unavailable.");
        var activeDraft = await db.WorkpaperDrafts.FromSqlInterpolated($"""
            SELECT * FROM workpaper_drafts
            WHERE firm_id = {target.FirmId} AND client_id = {target.ClientId}
              AND engagement_id = {target.EngagementId} AND workpaper_id = {target.Id}
              AND owner_user_id = {actor.UserId}
            FOR UPDATE
            """).SingleOrDefaultAsync(ct);
        if (req.ExpectedDraftRevision is { } expectedDraftRevision)
        {
            if (activeDraft is null)
                return CommandResult<WorkpaperResult>.Fail(ErrorCodes.Drafts.NotFound,
                    "The acknowledged draft no longer exists; reload before submitting.");
            if (activeDraft.Lifecycle != WorkpaperDraftLifecycles.Active)
                return CommandResult<WorkpaperResult>.Fail(ErrorCodes.Drafts.Consumed,
                    "The acknowledged draft is no longer active.");
            if (activeDraft.DraftRevision != expectedDraftRevision)
                return CommandResult<WorkpaperResult>.Fail(ErrorCodes.StaleRevision,
                    "The draft changed; save or reload before submitting.");
            if (req.DraftSaveId is { } saveId && activeDraft.LastSaveId != saveId)
                return CommandResult<WorkpaperResult>.Fail(ErrorCodes.IdempotencyConflict,
                    "The submitted save acknowledgement does not match the durable draft.");
            if (activeDraft.BaseWorkpaperRevision != target.Revision ||
                activeDraft.BaseInputGeneration != generations.InputGeneration ||
                activeDraft.BasePolicyGeneration != generations.PolicyGeneration)
                return CommandResult<WorkpaperResult>.Fail(ErrorCodes.Drafts.TargetChanged,
                    "The workpaper target or its generations changed; reload before submitting.");
            if (!string.Equals(activeDraft.WorkPerformed, req.WorkPerformed.Trim(), StringComparison.Ordinal) ||
                !string.Equals(activeDraft.Conclusion, req.Conclusion.Trim(), StringComparison.Ordinal))
                return CommandResult<WorkpaperResult>.Fail(ErrorCodes.IdempotencyConflict,
                    "Submission content must match the acknowledged durable draft.");
        }

        if (target.Revision == long.MaxValue) return CommandResult<WorkpaperResult>.Fail(ErrorCodes.ProtectedState,"The workpaper revision limit has been reached.");
        var revision = target.Revision + 1;
        var now = DateTimeOffset.UtcNow;
        var workPerformed = req.WorkPerformed.Trim();
        var conclusion = req.Conclusion.Trim();

        await db.Workpapers.Where(x => x.Id == req.WorkpaperId).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.Status, WorkpaperStatuses.SubmittedSnapshot)
            .SetProperty(x => x.Revision, revision)
            .SetProperty(x => x.WorkPerformed, workPerformed)
            .SetProperty(x => x.Conclusion, conclusion)
            .SetProperty(x => x.SubmittedAt, now), ct);

        if (target.ProcedureId is { } procedureId)
        {
            await db.AuditProcedures.Where(p => p.Id == procedureId && p.FirmId == target.FirmId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Status, AuditProcedureStatuses.Submitted)
                    .SetProperty(p => p.CurrentResultRevision, revision), ct);
        }

        db.WorkpaperSubmissions.Add(new WorkpaperSubmission
        {
            Id = Guid.CreateVersion7(),
            FirmId = target.FirmId,
            ClientId = target.ClientId,
            EngagementId = target.EngagementId,
            WorkpaperId = target.Id,
            ActorId = actor.UserId,
            Revision = revision,
            WorkPerformed = workPerformed,
            Conclusion = conclusion,
            SubmittedAt = now
        });
        if (activeDraft?.Lifecycle == WorkpaperDraftLifecycles.Active)
            activeDraft.Lifecycle = WorkpaperDraftLifecycles.Consumed;
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);

        return CommandResult<WorkpaperResult>.Ok(new WorkpaperResult(
            target.Id, target.Index, target.Title, WorkpaperStatuses.SubmittedSnapshot, revision));
    }

    /// <summary>
    /// Reads the durable draft for the authenticated owner. The browser circuit is only a
    /// convenience cache; the returned content is always sourced from PostgreSQL.
    /// </summary>
    public static async Task<CommandResult<WorkpaperDraftResult>> LoadWorkpaperDraftAsync(
        IAuditSphereDbContext db,
        ActorContext actor,
        Guid workpaperId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        using var activity = AuditDiagnostics.StartCommand("audit.workpaper.draft.load");
        activity?.SetTag("command", "workpaper.draft.load");

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var snapshot = await db.Workpapers.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == workpaperId && x.FirmId == actor.FirmId, ct);
        if (snapshot is null)
            return CommandResult<WorkpaperDraftResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

        var publication = await AuditPlanningCommandWorkspace.LockAsync(db, actor, snapshot.EngagementId, true, ct);
        if (!publication.Succeeded) return CommandResult<WorkpaperDraftResult>.Fail(publication.ErrorCode!, publication.Message!);
        var scope = await LockedEngagementAsync(db, actor, snapshot.EngagementId, ct, snapshot.ClientId);
        if (scope.Denied is not null)
            return CommandResult<WorkpaperDraftResult>.Fail(scope.Denied, scope.Message);
        var target = await LockedWorkpaperAsync(db, actor.FirmId, workpaperId, ct);
        if (target is null || target.ClientId != snapshot.ClientId || target.EngagementId != snapshot.EngagementId)
            return CommandResult<WorkpaperDraftResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

        var generations = await ReadGenerationsAsync(db, target.FirmId, target.ClientId, ct);
        if (generations.InputGeneration < 1 || generations.PolicyGeneration < 1)
            return CommandResult<WorkpaperDraftResult>.Fail(ErrorCodes.GateBlocked,"Workpaper safety state is unavailable.");
        var draft = await db.WorkpaperDrafts.AsNoTracking().SingleOrDefaultAsync(x =>
            x.FirmId == target.FirmId && x.ClientId == target.ClientId &&
            x.EngagementId == target.EngagementId && x.WorkpaperId == target.Id &&
            x.OwnerUserId == actor.UserId, ct);

        if (tx is not null) await tx.CommitAsync(ct);

        if (draft is null || draft.Lifecycle != WorkpaperDraftLifecycles.Active)
            return CommandResult<WorkpaperDraftResult>.Ok(new WorkpaperDraftResult(
                target.Id, null, target.Revision, generations.InputGeneration, generations.PolicyGeneration,
                0, target.WorkPerformed ?? string.Empty, target.Conclusion ?? string.Empty,
                Guid.Empty, null, draft?.Lifecycle ?? "NONE"));

        var targetChanged = draft.BaseWorkpaperRevision != target.Revision ||
                            draft.BaseInputGeneration != generations.InputGeneration ||
                            draft.BasePolicyGeneration != generations.PolicyGeneration;
        return CommandResult<WorkpaperDraftResult>.Ok(new WorkpaperDraftResult(
            draft.WorkpaperId, draft.Id, draft.BaseWorkpaperRevision, draft.BaseInputGeneration,
            draft.BasePolicyGeneration, draft.DraftRevision, draft.WorkPerformed, draft.Conclusion,
            draft.LastSaveId, draft.LastSavedAt,
            targetChanged ? "TARGET_CHANGED" : draft.Lifecycle));
    }

    /// <summary>
    /// Saves one acknowledged editor snapshot. Save IDs make retries idempotent, while the
    /// draft revision prevents an older autosave from overwriting a newer one.
    /// </summary>
    internal static async Task<CommandResult<WorkpaperDraftResult>> SaveWorkpaperDraftCoreAsync(
        IAuditSphereDbContext db,
        ActorContext actor,
        SaveWorkpaperDraftRequest req,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(req);
        var started = Stopwatch.GetTimestamp();
        using var activity = AuditDiagnostics.StartCommand("audit.workpaper.draft.save");
        activity?.SetTag("command", "workpaper.draft.save");

        var invalid = ValidateDraft(req);
        if (invalid is not null)
            return CommandResult<WorkpaperDraftResult>.Fail(ErrorCodes.Drafts.Conflict, invalid);

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var snapshot = await db.Workpapers.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == req.WorkpaperId && x.FirmId == actor.FirmId, ct);
        if (snapshot is null)
            return CommandResult<WorkpaperDraftResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

        var publication = await AuditPlanningCommandWorkspace.LockAsync(db, actor, snapshot.EngagementId, true, ct);
        if (!publication.Succeeded) return CommandResult<WorkpaperDraftResult>.Fail(publication.ErrorCode!, publication.Message!);
        var scope = await LockedEngagementAsync(db, actor, snapshot.EngagementId, ct, snapshot.ClientId);
        if (scope.Denied is not null)
            return CommandResult<WorkpaperDraftResult>.Fail(scope.Denied, scope.Message);
        var target = await LockedWorkpaperAsync(db, actor.FirmId, req.WorkpaperId, ct);
        if (target is null || target.ClientId != snapshot.ClientId || target.EngagementId != snapshot.EngagementId)
            return CommandResult<WorkpaperDraftResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
        if (target.Status != WorkpaperStatuses.Working)
            return CommandResult<WorkpaperDraftResult>.Fail(ErrorCodes.ProtectedState,
                "A submitted workpaper cannot accept a new draft.");
        if (target.Revision != req.BaseWorkpaperRevision)
            return CommandResult<WorkpaperDraftResult>.Fail(ErrorCodes.StaleRevision,
                "The workpaper changed; reload the current target before saving.");

        var generations = await ReadGenerationsAsync(db, target.FirmId, target.ClientId, ct);
        if (generations.InputGeneration < 1 || generations.PolicyGeneration < 1)
            return CommandResult<WorkpaperDraftResult>.Fail(ErrorCodes.GateBlocked,"Workpaper safety state is unavailable.");
        if (generations.InputGeneration != req.BaseInputGeneration ||
            generations.PolicyGeneration != req.BasePolicyGeneration)
            return CommandResult<WorkpaperDraftResult>.Fail(ErrorCodes.GenerationStale,
                "The engagement inputs or policy changed; reload the workpaper before saving.");

        var draft = await db.WorkpaperDrafts.FromSqlInterpolated($"""
            SELECT * FROM workpaper_drafts
            WHERE firm_id = {target.FirmId} AND client_id = {target.ClientId}
              AND engagement_id = {target.EngagementId} AND workpaper_id = {target.Id}
              AND owner_user_id = {actor.UserId}
            FOR UPDATE
            """).SingleOrDefaultAsync(ct);

        var workPerformed = req.WorkPerformed.Trim();
        var conclusion = req.Conclusion.Trim();
        if (draft?.DraftRevision == long.MaxValue) return CommandResult<WorkpaperDraftResult>.Fail(ErrorCodes.ProtectedState,"The draft revision limit has been reached.");
        if (draft is not null)
        {
            if (draft.Lifecycle == WorkpaperDraftLifecycles.Consumed)
                return CommandResult<WorkpaperDraftResult>.Fail(
                    ErrorCodes.Drafts.Consumed,
                    "The durable draft is no longer editable.");
            if (draft.Lifecycle == WorkpaperDraftLifecycles.Discarded)
            {
                // A discarded row may be intentionally started again from the clean
                // editor state, but a late autosave from the old revision must never
                // resurrect it. The UI starts a new lifecycle with expected revision 0.
                if (req.ExpectedDraftRevision != 0)
                    return CommandResult<WorkpaperDraftResult>.Fail(ErrorCodes.StaleRevision,
                        "The discarded draft is stale; start a new draft from the current workpaper.");
                draft.BaseWorkpaperRevision = req.BaseWorkpaperRevision;
                draft.BaseInputGeneration = req.BaseInputGeneration;
                draft.BasePolicyGeneration = req.BasePolicyGeneration;
                draft.Lifecycle = WorkpaperDraftLifecycles.Active;
                draft.DraftRevision++;
                draft.WorkPerformed = workPerformed;
                draft.Conclusion = conclusion;
                draft.LastSaveId = req.SaveId;
                draft.LastSavedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                if (draft.BaseWorkpaperRevision != req.BaseWorkpaperRevision ||
                    draft.BaseInputGeneration != req.BaseInputGeneration ||
                    draft.BasePolicyGeneration != req.BasePolicyGeneration)
                    return CommandResult<WorkpaperDraftResult>.Fail(ErrorCodes.Drafts.TargetChanged,
                        "The draft is based on a changed workpaper target; reload it before saving.");
                if (draft.LastSaveId == req.SaveId)
                {
                    if (!string.Equals(draft.WorkPerformed, workPerformed, StringComparison.Ordinal) ||
                        !string.Equals(draft.Conclusion, conclusion, StringComparison.Ordinal))
                        return CommandResult<WorkpaperDraftResult>.Fail(ErrorCodes.IdempotencyConflict,
                            "The save ID was already used for different draft content.");
                    if (tx is not null) await tx.CommitAsync(ct);
                    AuditDiagnostics.DraftSaveDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds,
                        new KeyValuePair<string, object?>[] { new("outcome", "idempotent") });
                    return CommandResult<WorkpaperDraftResult>.Ok(ToDraftResult(draft));
                }
                if (req.ExpectedDraftRevision != draft.DraftRevision)
                    return CommandResult<WorkpaperDraftResult>.Fail(ErrorCodes.StaleRevision,
                        "A newer draft revision has already been saved; reload before retrying.");

                draft.DraftRevision++;
                draft.WorkPerformed = workPerformed;
                draft.Conclusion = conclusion;
                draft.LastSaveId = req.SaveId;
                draft.LastSavedAt = DateTimeOffset.UtcNow;
            }
        }
        else
        {
            if (req.ExpectedDraftRevision is not 0 and not 1)
                return CommandResult<WorkpaperDraftResult>.Fail(ErrorCodes.StaleRevision,
                    "The initial draft revision is stale; reload the workpaper.");
            draft = new WorkpaperDraft
            {
                Id = Guid.CreateVersion7(),
                FirmId = target.FirmId,
                ClientId = target.ClientId,
                EngagementId = target.EngagementId,
                WorkpaperId = target.Id,
                OwnerUserId = actor.UserId,
                BaseWorkpaperRevision = req.BaseWorkpaperRevision,
                BaseInputGeneration = req.BaseInputGeneration,
                BasePolicyGeneration = req.BasePolicyGeneration,
                DraftRevision = 1,
                WorkPerformed = workPerformed,
                Conclusion = conclusion,
                LastSaveId = req.SaveId,
                LastSavedAt = DateTimeOffset.UtcNow,
                Lifecycle = WorkpaperDraftLifecycles.Active
            };
            db.WorkpaperDrafts.Add(draft);
        }

        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        AuditDiagnostics.DraftSaveDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds,
            new KeyValuePair<string, object?>[] { new("outcome", "saved") });
        return CommandResult<WorkpaperDraftResult>.Ok(ToDraftResult(draft));
    }

    /// <summary>Marks a draft discarded without deleting its audit-relevant lifecycle row.</summary>
    internal static async Task<CommandResult> DiscardWorkpaperDraftCoreAsync(
        IAuditSphereDbContext db,
        ActorContext actor,
        Guid workpaperId,
        long expectedDraftRevision,
        CancellationToken ct = default)
    {
        if (workpaperId == Guid.Empty || expectedDraftRevision < 1)
            return CommandResult.Fail(ErrorCodes.Drafts.Conflict, "A workpaper and positive draft revision are required.");

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var snapshot = await db.Workpapers.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == workpaperId && x.FirmId == actor.FirmId, ct);
        if (snapshot is null)
            return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
        var publication = await AuditPlanningCommandWorkspace.LockAsync(db, actor, snapshot.EngagementId, true, ct);
        if (!publication.Succeeded) return publication;
        var scope = await LockedEngagementAsync(db, actor, snapshot.EngagementId, ct, snapshot.ClientId);
        if (scope.Denied is not null)
            return CommandResult.Fail(scope.Denied, scope.Message);
        var draft = await db.WorkpaperDrafts.FromSqlInterpolated($"""
            SELECT * FROM workpaper_drafts
            WHERE firm_id = {actor.FirmId} AND client_id = {snapshot.ClientId}
              AND engagement_id = {snapshot.EngagementId} AND workpaper_id = {workpaperId}
              AND owner_user_id = {actor.UserId}
            FOR UPDATE
            """).SingleOrDefaultAsync(ct);
        if (draft is null)
            return CommandResult.Fail(ErrorCodes.Drafts.NotFound, "No durable draft exists for this workpaper.");
        if (draft.Lifecycle != WorkpaperDraftLifecycles.Active)
            return CommandResult.Fail(ErrorCodes.ProtectedState, "The durable draft is no longer editable.");
        if (draft.DraftRevision != expectedDraftRevision)
            return CommandResult.Fail(ErrorCodes.StaleRevision, "The draft changed; reload it before discarding.");

        draft.Lifecycle = WorkpaperDraftLifecycles.Discarded;
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return CommandResult.Ok();
    }


}
