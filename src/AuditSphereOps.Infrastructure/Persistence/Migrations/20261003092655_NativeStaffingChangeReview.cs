using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NativeStaffingChangeReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "staffing_changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    review_basis = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    preview_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staffing_changes", x => x.id);
                    table.CheckConstraint("ck_staffing_change", "actor_epoch >= 1 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND action IN ('ASSIGN','REVOKE') AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND length(preview_json) BETWEEN 1 AND 200000");
                    table.ForeignKey(
                        name: "FK_staffing_changes_engagement_staff_assignments_assignment_id",
                        column: x => x.assignment_id,
                        principalTable: "engagement_staff_assignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_staffing_changes_engagements_engagement_id",
                        column: x => x.engagement_id,
                        principalTable: "engagements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_staffing_changes_users_firm_id_actor_id",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_staffing_changes_users_firm_id_target_user_id",
                        columns: x => new { x.firm_id, x.target_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_staffing_changes_assignment_id",
                table: "staffing_changes",
                column: "assignment_id");

            migrationBuilder.CreateIndex(
                name: "IX_staffing_changes_engagement_id",
                table: "staffing_changes",
                column: "engagement_id");

            migrationBuilder.CreateIndex(
                name: "IX_staffing_changes_firm_id_actor_id_request_id",
                table: "staffing_changes",
                columns: new[] { "firm_id", "actor_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_staffing_changes_firm_id_target_user_id",
                table: "staffing_changes",
                columns: new[] { "firm_id", "target_user_id" });
            migrationBuilder.Sql("""
                CREATE TRIGGER staffing_change_immutable BEFORE UPDATE OR DELETE ON staffing_changes
                  FOR EACH ROW EXECUTE FUNCTION prevent_completion_evidence_mutation();
                CREATE FUNCTION enforce_staffing_change() RETURNS trigger AS $$
                DECLARE p jsonb := NEW.preview_json::jsonb;
                BEGIN
                  IF NOT EXISTS (
                    SELECT 1 FROM engagement_staff_assignments a
                    JOIN engagements e ON e.id=a.engagement_id AND e.firm_id=NEW.firm_id
                    JOIN client_safety_states c ON c.id=e.practice_client_id AND c.firm_id=NEW.firm_id
                    JOIN users u ON u.id=NEW.actor_id AND u.firm_id=NEW.firm_id
                    JOIN users t ON t.id=NEW.target_user_id AND t.firm_id=NEW.firm_id
                    JOIN role_grants g ON g.id=a.role_grant_id AND g.firm_id=NEW.firm_id
                    WHERE a.id=NEW.assignment_id AND a.firm_id=NEW.firm_id AND a.engagement_id=NEW.engagement_id
                      AND a.client_id=e.practice_client_id AND a.user_id=NEW.target_user_id
                      AND g.user_id=a.user_id AND g.client_id=a.client_id AND g.engagement_id=a.engagement_id
                      AND a.staffing_level=p->'Fields'->>'Level'
                      AND (p->'Fields'->>'UserId')::uuid=a.user_id
                      AND p->'Fields'->>'Action'=NEW.action
                      AND (p->>'EngagementId')::uuid=NEW.engagement_id
                      AND (p->>'RequestId')::uuid=NEW.request_id
                      AND p->>'RequestHash'=NEW.request_hash AND p->>'ReviewBasis'=NEW.review_basis
                      AND e.generation=(p->>'EngagementGeneration')::bigint
                      AND c.input_generation=(p->>'ClientGeneration')::bigint
                      AND u.session_epoch=NEW.actor_epoch AND NOT u.disabled AND lower(u.user_kind)<>'client'
                      AND EXISTS(SELECT 1 FROM role_grants r WHERE r.firm_id=NEW.firm_id AND r.user_id=NEW.actor_id
                        AND lower(r.role) IN ('administrator','partner','manager') AND r.revoked_at IS NULL
                        AND (r.expires_at IS NULL OR r.expires_at>statement_timestamp())
                        AND ((r.engagement_id=e.id AND r.client_id=e.practice_client_id)
                          OR (r.engagement_id IS NULL AND (r.client_id IS NULL OR r.client_id=e.practice_client_id))))
                      AND ((NEW.action='ASSIGN' AND a.assigned_by_user_id=NEW.actor_id AND a.revoked_at IS NULL
                          AND a.assigned_at<=NEW.created_at AND a.user_id<>NEW.actor_id AND NOT t.disabled
                          AND g.revoked_at IS NULL AND (g.expires_at IS NULL OR g.expires_at>statement_timestamp()))
                        OR (NEW.action='REVOKE' AND a.revoked_by_user_id=NEW.actor_id AND a.revoked_at IS NOT NULL
                          AND a.revoked_at<=NEW.created_at AND (p->'Fields'->>'AssignmentId')::uuid=a.id
                          AND (NOT (p->>'RemovesLocalGrant')::boolean OR
                            (g.revoked_at IS NOT NULL AND t.session_epoch>(p->>'TargetEpoch')::bigint))))
                  ) THEN RAISE EXCEPTION 'Staffing change requires exact authorized committed assignment' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE CONSTRAINT TRIGGER staffing_change_publication_guard AFTER INSERT ON staffing_changes
                  DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION enforce_staffing_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS(SELECT 1 FROM staffing_changes) THEN RAISE EXCEPTION 'Retained staffing evidence prevents rollback'; END IF;
                END $$;
                DROP FUNCTION enforce_staffing_change() CASCADE;
                """);
            migrationBuilder.DropTable(
                name: "staffing_changes");
        }
    }
}
