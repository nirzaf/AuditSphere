using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NativeEngagementCreationReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "engagement_creations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    review_basis = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    client_generation = table.Column<long>(type: "bigint", nullable: false),
                    engagement_generation = table.Column<long>(type: "bigint", nullable: false),
                    input_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_engagement_creations", x => x.id);
                    table.CheckConstraint("ck_engagement_creation", "actor_epoch >= 1 AND client_generation >= 1 AND engagement_generation = 1 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND length(input_json) BETWEEN 1 AND 2000");
                    table.ForeignKey(
                        name: "FK_engagement_creations_engagements_engagement_id",
                        column: x => x.engagement_id,
                        principalTable: "engagements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_engagement_creations_practice_clients_firm_id_client_id",
                        columns: x => new { x.firm_id, x.client_id },
                        principalTable: "practice_clients",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_engagement_creations_users_firm_id_actor_id",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_engagement_creations_engagement_id",
                table: "engagement_creations",
                column: "engagement_id");

            migrationBuilder.CreateIndex(
                name: "IX_engagement_creations_firm_id_actor_id_request_id",
                table: "engagement_creations",
                columns: new[] { "firm_id", "actor_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_engagement_creations_firm_id_client_id",
                table: "engagement_creations",
                columns: new[] { "firm_id", "client_id" });

            migrationBuilder.CreateIndex(
                name: "IX_engagement_creations_firm_id_engagement_id",
                table: "engagement_creations",
                columns: new[] { "firm_id", "engagement_id" },
                unique: true);
            migrationBuilder.Sql("""
                CREATE TRIGGER engagement_creations_append_only BEFORE UPDATE OR DELETE ON engagement_creations
                  FOR EACH ROW EXECUTE FUNCTION prevent_completion_evidence_mutation();
                CREATE FUNCTION enforce_engagement_creation() RETURNS trigger AS $$
                BEGIN
                  IF NOT EXISTS (
                    SELECT 1 FROM engagements e
                    JOIN client_safety_states c ON c.firm_id=e.firm_id AND c.id=e.practice_client_id
                    JOIN users u ON u.firm_id=e.firm_id AND u.id=NEW.actor_id
                    WHERE e.firm_id=NEW.firm_id AND e.id=NEW.engagement_id AND e.practice_client_id=NEW.client_id
                    AND e.status='Draft' AND e.professional_work_blocked AND e.generation=NEW.engagement_generation
                    AND e.service_route=NEW.input_json::jsonb->>'ServiceRoute'
                    AND e.service_profile_id=NEW.input_json::jsonb->>'ServiceProfile'
                    AND e.period_start=NEW.input_json::jsonb->>'PeriodStart' AND e.period_end=NEW.input_json::jsonb->>'PeriodEnd'
                    AND e.created_at=NEW.created_at AND c.input_generation=NEW.client_generation
                    AND u.session_epoch=NEW.actor_epoch AND NOT u.disabled AND lower(u.user_kind)<>'client'
                    AND EXISTS (SELECT 1 FROM role_grants r WHERE r.firm_id=e.firm_id AND r.user_id=u.id
                      AND lower(r.role) IN ('partner','manager') AND r.revoked_at IS NULL
                      AND (r.expires_at IS NULL OR r.expires_at>statement_timestamp())
                      AND r.engagement_id IS NULL AND (r.client_id IS NULL OR r.client_id=e.practice_client_id))
                  ) THEN RAISE EXCEPTION 'Creation receipt requires exact current scoped blocked publication' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE CONSTRAINT TRIGGER engagement_creation_publication_guard AFTER INSERT ON engagement_creations
                  DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION enforce_engagement_creation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS(SELECT 1 FROM engagement_creations) THEN
                    RAISE EXCEPTION 'Retained engagement creation evidence prevents rollback';
                  END IF;
                END $$;
                DROP FUNCTION enforce_engagement_creation() CASCADE;
                """);
            migrationBuilder.DropTable(
                name: "engagement_creations");
        }
    }
}
