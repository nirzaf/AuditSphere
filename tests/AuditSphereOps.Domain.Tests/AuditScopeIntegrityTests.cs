using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// Scope integrity for the audit-planning tables (§27.4, §42.3, §42.4): composite scope foreign keys,
/// mandatory scope columns, database-level immutability of frozen evidence, in-command authorization,
/// and a migration that refuses ambiguous legacy history instead of backfilling an invented scope.
/// </summary>
[Trait("Profile", "Database")]
public sealed class AuditScopeIntegrityTests
{
    private const string BeforeScopeIntegrity = "20260918235000_AuditPlanningAndFirmPostingExtensions";

    // ── NT-11: cross-scope child links ──────────────────────────────────────

    [Fact(DisplayName = "NT-11: A workpaper pointing at another client's engagement is rejected by PostgreSQL")]
    public async Task CrossClientEngagementLink_Rejected()
    {
        await using var pg = await PgTestSchema.CreateAsync();
        var fixture = await PlanningSeed.CreateAsync(pg);
        var (firmId, clientId, engagementId) = (fixture.Primary.FirmId, fixture.Primary.ClientId, fixture.Primary.EngagementId);
        var otherEngagement = fixture.Other.EngagementId;

        await using var db = new AuditSphereDbContext(pg.Options);

        // The client column contradicts the engagement's stored client.
        var mismatched = new Workpaper
        {
            Id = Guid.NewGuid(), FirmId = firmId, ClientId = clientId, EngagementId = otherEngagement,
            ActorId = fixture.Primary.Actor.UserId, Index = "X-01", Title = "Mismatched",
            Objective = "Intent test", TemplateVersion = "T-1", Procedure = "n/a",
            Revision = 1, Status = WorkpaperStatuses.Working, CreatedAt = DateTimeOffset.UtcNow
        };
        db.Workpapers.Add(mismatched);
        var efRefusal = await Record.ExceptionAsync(() => db.SaveChangesAsync());
        Assert.IsType<PostgresException>(efRefusal!.GetBaseException());
        Assert.Equal("23503", ((PostgresException)efRefusal.GetBaseException()!).SqlState);
        db.ChangeTracker.Clear();

        // The same defect expressed directly in SQL fails identically.
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
          INSERT INTO workpapers (id, firm_id, client_id, engagement_id, actor_id, wp_index, title,
            objective, template_version, procedure, revision, status, created_at)
          VALUES ({Guid.NewGuid()}, {firmId}, {clientId}, {otherEngagement},
            {fixture.Primary.Actor.UserId}, 'X-02', 'Raw mismatch', 'Intent test', 'T-1', 'n/a', 1,
            'WORKING', statement_timestamp())
          """));
        Assert.Equal(0, await db.Workpapers.CountAsync());
    }

    [Fact(DisplayName = "NT-11b: An engagement cannot be deleted while its workpapers exist")]
    public async Task EngageDeletionRestrictedByEvidence()
    {
        await using var pg = await PgTestSchema.CreateAsync();
        var fixture = await PlanningSeed.CreateAsync(pg);
        var scope = fixture.Primary;

        await using var db = new AuditSphereDbContext(pg.Options);
        var created = await AuditPlanningService.CreateWorkpaperAsync(db, scope.Actor,
            new CreateWorkpaperRequest(scope.EngagementId, "D-01", "Deletion guard", "Objective",
                "T-1", null, "Procedure"));
        Assert.True(created.Succeeded);

        // §42.3: professional records never disappear through a cascade delete.
        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
          DELETE FROM engagements WHERE id = {scope.EngagementId}
          """));
        // ON DELETE RESTRICT reports a restrict violation rather than a plain foreign-key failure.
        Assert.Equal("23001", ex.SqlState);
        Assert.Equal(1, await db.Workpapers.CountAsync());
    }

    // ── NT-12: database-level immutability of frozen evidence ───────────────

    [Fact(DisplayName = "NT-12: Frozen planning evidence refuses UPDATE and DELETE at the database")]
    public async Task FrozenPlanningEvidence_IsAppendOnly()
    {
        await using var pg = await PgTestSchema.CreateAsync();
        var fixture = await PlanningSeed.CreateAsync(pg);
        var scope = fixture.Primary;

        await using var db = new AuditSphereDbContext(pg.Options);
        var materiality = await AuditPlanningService.CreateMaterialityAssessmentAsync(db, scope.Actor,
            new CreateMaterialityRequest(scope.EngagementId, "Total assets", "V1", "Rationale",
                1_000_000m, 0.05m, 50_000m, 40_000m, 2_500m, null));
        var population = await AuditPlanningService.CreatePopulationVersionAsync(db, scope.Actor,
            new CreatePopulationRequest(scope.EngagementId, "Purpose", "Existence", "RECEIPT-1",
                "parameters", 10, 1_000m, "QAR", null));
        var workpaper = await AuditPlanningService.CreateWorkpaperAsync(db, scope.Actor,
            new CreateWorkpaperRequest(scope.EngagementId, "A-01", "Title", "Objective", "T-1", null, "Steps"));
        var submitted = await AuditPlanningService.SubmitWorkpaperAsync(db, scope.Actor,
            new SubmitWorkpaperRequest(workpaper.Value!.WorkpaperId, 1, "Performed", "Concluded"));
        Assert.True(submitted.Succeeded);

        async Task Refused(string sql)
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(
                () => db.Database.ExecuteSqlRawAsync(sql));
            Assert.Equal("55000", ex.SqlState);
        }

        await Refused($"UPDATE materiality_assessments SET rationale = 'rewritten'");
        await Refused($"DELETE FROM materiality_assessments");
        await Refused($"UPDATE population_versions SET row_count = 99999");
        await Refused($"DELETE FROM population_versions");
        await Refused($"UPDATE workpaper_submissions SET conclusion = 'rewritten'");
        await Refused($"DELETE FROM workpaper_submissions");
        await Refused($"UPDATE workpapers SET conclusion = 'rewritten'");
        await Refused($"DELETE FROM workpapers");

        Assert.Equal("Rationale", (await db.MaterialityAssessments.AsNoTracking().SingleAsync()).Rationale);
        Assert.Equal("Concluded", (await db.WorkpaperSubmissions.AsNoTracking().SingleAsync()).Conclusion);
    }

    [Fact(DisplayName = "NT-12b: Working content stays editable until it is frozen")]
    public async Task WorkingWorkpaper_RemainsEditable()
    {
        await using var pg = await PgTestSchema.CreateAsync();
        var fixture = await PlanningSeed.CreateAsync(pg);
        var scope = fixture.Primary;

        await using var db = new AuditSphereDbContext(pg.Options);
        var created = await AuditPlanningService.CreateWorkpaperAsync(db, scope.Actor,
            new CreateWorkpaperRequest(scope.EngagementId, "W-01", "Editable", "Objective", "T-1", null, "Steps"));

        await db.Database.ExecuteSqlInterpolatedAsync($"""
          UPDATE workpapers SET work_performed = {("drafted note")} WHERE id = {created.Value!.WorkpaperId}
          """);
        Assert.Equal("drafted note", (await db.Workpapers.AsNoTracking().SingleAsync()).WorkPerformed);
    }

    // ── §42.3/§42.4: mandatory scope columns and model ownership ────────────

    [Fact(DisplayName = "IG-01: Scope columns are mandatory and every scoped table carries a composite key")]
    public async Task ScopeColumns_AreMandatoryAndConstrained()
    {
        await using var pg = await PgTestSchema.CreateAsync();
        await using var db = new AuditSphereDbContext(pg.Options);

        foreach (var table in new[] { "audit_risks", "workpapers", "findings", "materiality_assessments",
          "population_versions", "workpaper_submissions", "source_receipts", "evidence_links",
          "engagement_assignments", "mapping_rules", "review_points", "archives", "record_states",
          "eqr_cases", "written_representations" })
        {
          Assert.Equal(1, await ScalarAsync(db,
            "SELECT count(*) FROM information_schema.columns WHERE table_schema = current_schema() " +
            "AND table_name = " + $"'{table}' AND column_name = 'firm_id' AND is_nullable = 'NO'"));
          // At least one foreign key must carry firm_id; a table may legitimately hold several
          // (its engagement scope plus user references), all composite.
          Assert.True(await ScalarAsync(db,
            "SELECT count(*) FROM pg_constraint k JOIN pg_class c ON c.oid = k.conrelid " +
            "JOIN pg_namespace n ON n.oid = c.relnamespace AND n.nspname = current_schema() " +
            $"WHERE c.relname = '{table}' AND k.contype = 'f' AND k.conname LIKE '%firm_id%'") >= 1,
            $"{table} has no firm-scoped foreign key.");
        }

        // The three formerly model-external tables are now owned by the EF model.
        foreach (var table in new[] { "materiality_assessments", "population_versions", "workpaper_submissions" })
          Assert.Equal(1, await ScalarAsync(db,
            "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace " +
            $"WHERE c.relname = '{table}' AND n.nspname = current_schema() AND c.relkind = 'r'"));
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact(DisplayName = "IG-02: A NULL scope column in a risk row is rejected by the database")]
    public async Task NullScope_RejectedByDatabase()
    {
        await using var pg = await PgTestSchema.CreateAsync();
        var fixture = await PlanningSeed.CreateAsync(pg);
        await using var db = new AuditSphereDbContext(pg.Options);

        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
          INSERT INTO audit_risks (id, firm_id, client_id, engagement_id, actor_id, account_area,
            assertion, description, drivers, severity, significance_decision, response_description,
            status, created_at)
          VALUES ({Guid.NewGuid()}, NULL, {fixture.Primary.ClientId}, {fixture.Primary.EngagementId},
            {fixture.Primary.Actor.UserId}, 'Area', 'Existence', 'Description', 'Drivers', 'Normal',
            'NORMAL', 'Response', 'IDENTIFIED', statement_timestamp())
          """));
    }

    [Fact(DisplayName = "IG-03: A CHECK refuses a significance decision that disagrees with the stored severity")]
    public async Task SeverityMustFollowSignificance()
    {
        await using var pg = await PgTestSchema.CreateAsync();
        var fixture = await PlanningSeed.CreateAsync(pg);
        var scope = fixture.Primary;
        await using var db = new AuditSphereDbContext(pg.Options);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
          INSERT INTO audit_risks (id, firm_id, client_id, engagement_id, actor_id, account_area,
            assertion, description, drivers, severity, significance_decision, response_description,
            status, created_at)
          VALUES ({Guid.NewGuid()}, {scope.FirmId}, {scope.ClientId}, {scope.EngagementId},
            {scope.Actor.UserId}, 'Area', 'Existence', 'Description', 'Drivers', 'Significant',
            'NORMAL', 'Response', 'IDENTIFIED', statement_timestamp())
          """));
        Assert.Equal("23514", ex.SqlState);
    }

    // ── NT-14: one transaction, no split state ─────────────────────────────

    [Fact(DisplayName = "NT-14: A database-refused write leaves no partial planning state behind")]
    public async Task RejectedWrite_LeavesNoPartialState()
    {
        await using var pg = await PgTestSchema.CreateAsync();
        var fixture = await PlanningSeed.CreateAsync(pg);
        var scope = fixture.Primary;

        await using var db = new AuditSphereDbContext(pg.Options);
        // Passes every in-process guard, then violates the stored column width: the whole command
        // transaction must unwind, leaving neither a finding nor a half-applied mutation.
        var refused = await Record.ExceptionAsync(() =>
            AuditPlanningService.CreateFindingAsync(db, scope.Actor,
                new CreateFindingRequest(scope.EngagementId, new string('T', 400), "Impact", false, 10m, null)));

        Assert.IsType<PostgresException>(refused!.GetBaseException());
        Assert.Equal("22001", ((PostgresException)refused.GetBaseException()!).SqlState);
        Assert.Equal(0, await db.Findings.CountAsync());
        Assert.Equal(0, await db.MaterialityAssessments.CountAsync());
    }

    // ── In-command authorization ───────────────────────────────────────────

    [Fact(DisplayName = "AUTH: An actor without a covering grant is denied nondisclosingly")]
    public async Task MissingGrant_Denied()
    {
        await using var pg = await PgTestSchema.CreateAsync();
        var fixture = await PlanningSeed.CreateAsync(pg);
        var foreign = fixture.Other;   // no role grant covers this engagement
        var known = fixture.Primary.EngagementId;

        await using var db = new AuditSphereDbContext(pg.Options);
        var ungranted = await AuditPlanningService.CreateMaterialityAssessmentAsync(db, foreign.Actor,
            new CreateMaterialityRequest(foreign.EngagementId, "Revenue", "V1", "R", 1m, 0.05m, 1m, 0.5m, 0.1m, null));
        Assert.False(ungranted.Succeeded);
        Assert.Equal(ErrorCodes.ScopeDenied, ungranted.ErrorCode);

        // A guessed identifier in another firm's scope must not disclose anything either.
        var stranger = new ActorContext(Guid.NewGuid(), Guid.NewGuid(), 1, ["Senior"]);
        var crossFirm = await AuditPlanningService.CreateAuditRiskAsync(db, stranger,
            new CreateAuditRiskRequest(known, "Area", "Existence", "Description", "Drivers",
                SignificanceDecisions.Normal, null, "Response"));
        Assert.False(crossFirm.Succeeded);
        Assert.Equal(ErrorCodes.ScopeDenied, crossFirm.ErrorCode);
        Assert.Equal("Access denied.", crossFirm.Message);
        Assert.Equal(0, await db.AuditRisks.CountAsync());
    }

    [Fact(DisplayName = "AUTH: A role that is not permitted for planning work is denied")]
    public async Task DisallowedRole_Denied()
    {
        await using var pg = await PgTestSchema.CreateAsync();
        var fixture = await PlanningSeed.CreateAsync(pg, role: "ClientUser");
        await using var db = new AuditSphereDbContext(pg.Options);

        var result = await AuditPlanningService.CreateMaterialityAssessmentAsync(db, fixture.Primary.Actor,
            new CreateMaterialityRequest(fixture.Primary.EngagementId, "Revenue", "V1", "R",
              1_000_000m, 0.05m, 50_000m, 40_000m, 2_500m, null));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.ScopeDenied, result.ErrorCode);
    }

    [Fact(DisplayName = "AUTH: Blocked professional work and an unreleased hold both refuse planning writes")]
    public async Task Gates_RefusePlanningWrites()
    {
        await using var pg = await PgTestSchema.CreateAsync();
        var blocked = await PlanningSeed.CreateAsync(pg, blocked: true);
        await using var db = new AuditSphereDbContext(pg.Options);

        var blockedResult = await AuditPlanningService.CreateFindingAsync(db, blocked.Primary.Actor,
            new CreateFindingRequest(blocked.Primary.EngagementId, "Type", "Impact", false, null, null));
        Assert.False(blockedResult.Succeeded);
        Assert.Equal(ErrorCodes.GateBlocked, blockedResult.ErrorCode);

        await using var pg2 = await PgTestSchema.CreateAsync();
        var held = await PlanningSeed.CreateAsync(pg2, withHold: true);
        await using var db2 = new AuditSphereDbContext(pg2.Options);
        var heldResult = await AuditPlanningService.CreateFindingAsync(db2, held.Primary.Actor,
            new CreateFindingRequest(held.Primary.EngagementId, "Type", "Impact", false, null, null));
        Assert.False(heldResult.Succeeded);
        Assert.Equal(ErrorCodes.GateBlocked, heldResult.ErrorCode);
    }

    // ── Migration preflight ────────────────────────────────────────────────

    [Fact(DisplayName = "MIG: Planning history whose scope the old schema cannot prove stops the migration")]
    public async Task PopulatedLegacyHistory_StopsMigrationBeforeModification()
    {
        await using var pg = await PgTestSchema.CreateAsync(BeforeScopeIntegrity);
        var (_, _, engagementId) = await pg.SeedScopeAsync();

        await using (var db = new AuditSphereDbContext(pg.Options))
        {
          await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO materiality_assessments (id, engagement_id, actor_id, benchmark_source,
              benchmark_version, rationale, benchmark_amount, rate_applied, overall_materiality,
              performance_materiality, clearly_trivial_threshold, status, created_at)
            VALUES ({Guid.NewGuid()}, {engagementId}, {Guid.NewGuid()}, 'Total assets', 'V1', 'Rationale',
              1000000, 0.05, 50000, 40000, 2500, 'DRAFT', statement_timestamp())
            """);
        }

        await using (var db = new AuditSphereDbContext(pg.Options))
        {
          var refusal = await Record.ExceptionAsync(() => db.Database.MigrateAsync());
          var ex = Assert.IsType<PostgresException>(refusal!.GetBaseException());
          Assert.Equal("55000", ex.SqlState);
          Assert.Contains("explicit disposition", ex.MessageText);
        }

        // The refusal left the legacy shape untouched, so an operator can still read it.
        await using (var db = new AuditSphereDbContext(pg.Options))
          Assert.Equal(1, await ScalarAsync(db,
            "SELECT count(*) FROM information_schema.columns WHERE table_schema = current_schema() " +
            "AND table_name = 'materiality_assessments' AND column_name = 'engagement_id'"));
    }

    private static async Task<int> ScalarAsync(AuditSphereDbContext db, string sql)
    {
      var connection = db.Database.GetDbConnection();
      if (connection.State != System.Data.ConnectionState.Open)
        await connection.OpenAsync();
      await using var command = connection.CreateCommand();
      command.CommandText = sql;
      var value = await command.ExecuteScalarAsync();
      return Convert.ToInt32(value);
    }
}
