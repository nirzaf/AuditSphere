using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StaffAllocationPhaseRiskArea : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_staff_allocations_firm_id_engagement_id_user_id_week_start",
                table: "staff_allocations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_staff_allocation_values",
                table: "staff_allocations");

            migrationBuilder.AddColumn<string>(
                name: "phase",
                table: "staff_allocations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "UNASSIGNED");

            migrationBuilder.AddColumn<string>(
                name: "risk_area",
                table: "staff_allocations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_staff_allocations_firm_id_engagement_id_user_id_week_start_~",
                table: "staff_allocations",
                columns: new[] { "firm_id", "engagement_id", "user_id", "week_start", "phase", "risk_area" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_staff_allocation_values",
                table: "staff_allocations",
                sql: "planned_minutes BETWEEN 1 AND 4800 AND extract(dow from week_start) = 0 AND phase IN ('PLANNING','FIELDWORK','COMPLETION','REPORTING','UNASSIGNED') AND risk_area = btrim(risk_area) AND length(risk_area) <= 100");

            // Preserve legacy receipt publication, but bind newly published allocations to the exact phase/risk line.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION enforce_resource_planning_receipt() RETURNS trigger AS $$
                DECLARE p jsonb := NEW.preview_json::jsonb; f jsonb := p->'Fields';
                BEGIN
                  IF NOT EXISTS (SELECT 1 FROM users u WHERE u.firm_id=NEW.firm_id AND u.id=NEW.actor_id
                    AND NOT u.disabled AND lower(u.user_kind)='staff' AND u.session_epoch=NEW.actor_epoch
                    AND EXISTS (SELECT 1 FROM role_grants g WHERE g.firm_id=u.firm_id AND g.user_id=u.id
                      AND g.client_id IS NULL AND g.engagement_id IS NULL AND g.revoked_at IS NULL
                      AND (g.expires_at IS NULL OR g.expires_at>transaction_timestamp())
                      AND lower(g.role) IN ('administrator','partner','manager')))
                    OR NOT EXISTS (SELECT 1 FROM users t WHERE t.id=NEW.target_user_id AND t.firm_id=NEW.firm_id
                      AND NOT t.disabled AND lower(t.user_kind)='staff')
                    OR (p->>'RequestId')::uuid IS DISTINCT FROM NEW.request_id
                    OR (f->>'UserId')::uuid IS DISTINCT FROM NEW.target_user_id
                    OR p->>'RequestHash' IS DISTINCT FROM NEW.request_hash OR p->>'ReviewBasis' IS DISTINCT FROM NEW.review_basis
                    OR f->>'Kind' IS DISTINCT FROM NEW.kind
                    OR (NEW.kind='ALLOCATION' AND (NOT (f ? 'Phase') OR NOT (f ? 'RiskArea')
                      OR (f->>'PlannedMinutes')::integer>0 AND f->>'Phase'='UNASSIGNED'))
                  THEN RAISE EXCEPTION 'Planning receipt requires current firm-wide authority and exact review' USING ERRCODE='23514'; END IF;
                  IF NOT (
                    (NEW.kind='PROFILE' AND EXISTS (SELECT 1 FROM staff_profiles r WHERE r.id=NEW.resource_id
                      AND r.firm_id=NEW.firm_id AND r.user_id=NEW.target_user_id AND r.updated_by_user_id=NEW.actor_id
                      AND r.department=f->>'Department' AND r.skills=coalesce(f->>'Skills','')
                      AND r.weekly_capacity_minutes=(f->>'WeeklyCapacityMinutes')::integer
                      AND r.target_utilization_percent=(f->>'TargetUtilizationPercent')::numeric))
                    OR (NEW.kind='CERTIFICATION' AND EXISTS (SELECT 1 FROM staff_certifications r WHERE r.id=NEW.resource_id
                      AND r.firm_id=NEW.firm_id AND r.user_id=NEW.target_user_id AND r.recorded_by_user_id=NEW.actor_id
                      AND r.name=f->>'Name' AND r.issuer IS NULL AND r.expires_on IS NOT DISTINCT FROM (f->>'ExpiresOn')::date))
                    OR (NEW.kind='AVAILABILITY' AND EXISTS (SELECT 1 FROM staff_availabilities r WHERE r.id=NEW.resource_id
                      AND r.firm_id=NEW.firm_id AND r.user_id=NEW.target_user_id AND r.recorded_by_user_id=NEW.actor_id
                      AND r.start_date=(f->>'StartDate')::date AND r.end_date=(f->>'EndDate')::date
                      AND r.kind=f->>'AvailabilityKind' AND r.minutes_per_day=(f->>'MinutesPerDay')::integer))
                    OR (NEW.kind='ALLOCATION' AND EXISTS (SELECT 1 FROM engagements e JOIN engagement_staff_assignments a
                      ON a.engagement_id=e.id AND a.firm_id=e.firm_id
                      WHERE e.firm_id=NEW.firm_id AND e.id=(f->>'EngagementId')::uuid AND e.status='Active'
                        AND a.user_id=NEW.target_user_id AND a.revoked_at IS NULL)
                      AND (((f->>'PlannedMinutes')::integer=0 AND NEW.resource_id IS NULL AND NOT EXISTS (
                        SELECT 1 FROM staff_allocations r WHERE r.firm_id=NEW.firm_id AND r.user_id=NEW.target_user_id
                          AND r.engagement_id=(f->>'EngagementId')::uuid AND r.week_start=(f->>'WeekStart')::date
                          AND r.phase=coalesce(f->>'Phase','UNASSIGNED') AND r.risk_area=coalesce(f->>'RiskArea','')))
                        OR ((f->>'PlannedMinutes')::integer>0 AND EXISTS (SELECT 1 FROM staff_allocations r
                          WHERE r.id=NEW.resource_id AND r.firm_id=NEW.firm_id AND r.user_id=NEW.target_user_id
                            AND r.updated_by_user_id=NEW.actor_id AND r.engagement_id=(f->>'EngagementId')::uuid
                            AND r.week_start=(f->>'WeekStart')::date AND r.phase=f->>'Phase'
                            AND r.risk_area=coalesce(f->>'RiskArea','') AND r.planned_minutes=(f->>'PlannedMinutes')::integer))))
                  ) THEN RAISE EXCEPTION 'Planning receipt requires the exact committed result' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM staff_allocations WHERE phase <> 'UNASSIGNED' OR risk_area <> '')
                    OR EXISTS (SELECT 1 FROM staff_allocations GROUP BY firm_id, engagement_id, user_id, week_start HAVING count(*) > 1)
                  THEN RAISE EXCEPTION 'Attributed staff allocations prevent rollback without losing planning detail' USING ERRCODE='23514'; END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_staff_allocations_firm_id_engagement_id_user_id_week_start_~",
                table: "staff_allocations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_staff_allocation_values",
                table: "staff_allocations");

            migrationBuilder.DropColumn(
                name: "phase",
                table: "staff_allocations");

            migrationBuilder.DropColumn(
                name: "risk_area",
                table: "staff_allocations");

            migrationBuilder.CreateIndex(
                name: "IX_staff_allocations_firm_id_engagement_id_user_id_week_start",
                table: "staff_allocations",
                columns: new[] { "firm_id", "engagement_id", "user_id", "week_start" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_staff_allocation_values",
                table: "staff_allocations",
                sql: "planned_minutes BETWEEN 1 AND 4800 AND extract(dow from week_start) = 0");

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION enforce_resource_planning_receipt() RETURNS trigger AS $$
                DECLARE p jsonb := NEW.preview_json::jsonb; f jsonb := p->'Fields';
                BEGIN
                  IF NOT EXISTS (SELECT 1 FROM users u WHERE u.firm_id=NEW.firm_id AND u.id=NEW.actor_id
                    AND NOT u.disabled AND lower(u.user_kind)='staff' AND u.session_epoch=NEW.actor_epoch
                    AND EXISTS (SELECT 1 FROM role_grants g WHERE g.firm_id=u.firm_id AND g.user_id=u.id
                      AND g.client_id IS NULL AND g.engagement_id IS NULL AND g.revoked_at IS NULL
                      AND (g.expires_at IS NULL OR g.expires_at>transaction_timestamp())
                      AND lower(g.role) IN ('administrator','partner','manager')))
                    OR NOT EXISTS (SELECT 1 FROM users t WHERE t.id=NEW.target_user_id AND t.firm_id=NEW.firm_id
                      AND NOT t.disabled AND lower(t.user_kind)='staff')
                    OR (p->>'RequestId')::uuid IS DISTINCT FROM NEW.request_id
                    OR (f->>'UserId')::uuid IS DISTINCT FROM NEW.target_user_id
                    OR p->>'RequestHash' IS DISTINCT FROM NEW.request_hash OR p->>'ReviewBasis' IS DISTINCT FROM NEW.review_basis
                    OR f->>'Kind' IS DISTINCT FROM NEW.kind
                  THEN RAISE EXCEPTION 'Planning receipt requires current firm-wide authority and exact review' USING ERRCODE='23514'; END IF;
                  IF NOT (
                    (NEW.kind='PROFILE' AND EXISTS (SELECT 1 FROM staff_profiles r WHERE r.id=NEW.resource_id
                      AND r.firm_id=NEW.firm_id AND r.user_id=NEW.target_user_id AND r.updated_by_user_id=NEW.actor_id
                      AND r.department=f->>'Department' AND r.skills=coalesce(f->>'Skills','')
                      AND r.weekly_capacity_minutes=(f->>'WeeklyCapacityMinutes')::integer
                      AND r.target_utilization_percent=(f->>'TargetUtilizationPercent')::numeric))
                    OR (NEW.kind='CERTIFICATION' AND EXISTS (SELECT 1 FROM staff_certifications r WHERE r.id=NEW.resource_id
                      AND r.firm_id=NEW.firm_id AND r.user_id=NEW.target_user_id AND r.recorded_by_user_id=NEW.actor_id
                      AND r.name=f->>'Name' AND r.issuer IS NULL AND r.expires_on IS NOT DISTINCT FROM (f->>'ExpiresOn')::date))
                    OR (NEW.kind='AVAILABILITY' AND EXISTS (SELECT 1 FROM staff_availabilities r WHERE r.id=NEW.resource_id
                      AND r.firm_id=NEW.firm_id AND r.user_id=NEW.target_user_id AND r.recorded_by_user_id=NEW.actor_id
                      AND r.start_date=(f->>'StartDate')::date AND r.end_date=(f->>'EndDate')::date
                      AND r.kind=f->>'AvailabilityKind' AND r.minutes_per_day=(f->>'MinutesPerDay')::integer))
                    OR (NEW.kind='ALLOCATION' AND EXISTS (SELECT 1 FROM engagements e JOIN engagement_staff_assignments a
                      ON a.engagement_id=e.id AND a.firm_id=e.firm_id
                      WHERE e.firm_id=NEW.firm_id AND e.id=(f->>'EngagementId')::uuid AND e.status='Active'
                        AND a.user_id=NEW.target_user_id AND a.revoked_at IS NULL)
                      AND (((f->>'PlannedMinutes')::integer=0 AND NEW.resource_id IS NULL AND NOT EXISTS (
                        SELECT 1 FROM staff_allocations r WHERE r.firm_id=NEW.firm_id AND r.user_id=NEW.target_user_id
                          AND r.engagement_id=(f->>'EngagementId')::uuid AND r.week_start=(f->>'WeekStart')::date))
                        OR ((f->>'PlannedMinutes')::integer>0 AND EXISTS (SELECT 1 FROM staff_allocations r
                          WHERE r.id=NEW.resource_id AND r.firm_id=NEW.firm_id AND r.user_id=NEW.target_user_id
                            AND r.updated_by_user_id=NEW.actor_id AND r.engagement_id=(f->>'EngagementId')::uuid
                            AND r.week_start=(f->>'WeekStart')::date AND r.planned_minutes=(f->>'PlannedMinutes')::integer))))
                  ) THEN RAISE EXCEPTION 'Planning receipt requires the exact committed result' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);
        }
    }
}
