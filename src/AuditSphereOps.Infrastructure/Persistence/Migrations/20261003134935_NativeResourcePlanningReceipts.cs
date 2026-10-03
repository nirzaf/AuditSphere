using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NativeResourcePlanningReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "resource_planning_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    target_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    review_basis = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    preview_json = table.Column<string>(type: "text", nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resource_planning_receipts", x => x.id);
                    table.CheckConstraint("ck_resource_planning_receipt", "actor_epoch >= 1 AND kind IN ('PROFILE','CERTIFICATION','AVAILABILITY','ALLOCATION') AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND length(preview_json) BETWEEN 1 AND 100000 AND (resource_id IS NOT NULL OR kind = 'ALLOCATION')");
                    table.ForeignKey(
                        name: "FK_resource_planning_receipts_users_firm_id_actor_id",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_resource_planning_receipts_users_firm_id_target_user_id",
                        columns: x => new { x.firm_id, x.target_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_resource_planning_receipts_firm_id_actor_id_request_id",
                table: "resource_planning_receipts",
                columns: new[] { "firm_id", "actor_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_resource_planning_receipts_firm_id_target_user_id",
                table: "resource_planning_receipts",
                columns: new[] { "firm_id", "target_user_id" });
            migrationBuilder.Sql("""
                CREATE TRIGGER resource_planning_receipt_immutable BEFORE UPDATE OR DELETE ON resource_planning_receipts
                  FOR EACH ROW EXECUTE FUNCTION prevent_completion_evidence_mutation();
                CREATE FUNCTION enforce_resource_planning_receipt() RETURNS trigger AS $$
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
                CREATE CONSTRAINT TRIGGER resource_planning_receipt_publication AFTER INSERT ON resource_planning_receipts
                  DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION enforce_resource_planning_receipt();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM resource_planning_receipts) THEN
                    RAISE EXCEPTION 'Retained planning evidence prevents rollback';
                  END IF;
                END $$;
                DROP FUNCTION enforce_resource_planning_receipt() CASCADE;
                """);
            migrationBuilder.DropTable(name: "resource_planning_receipts");
        }
    }
}
