using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordsActionEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_records_actions_firm_id_id",
                table: "records_actions",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_records_actions_scope_id",
                table: "records_actions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id" });

            migrationBuilder.CreateTable(
                name: "records_action_evidence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    records_action_id = table.Column<Guid>(type: "uuid", nullable: false),
                    archive_id = table.Column<Guid>(type: "uuid", nullable: false),
                    archive_manifest_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    event_kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    desired_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    desired_protection = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    observed_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    observed_protection = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    external_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    observed_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    exception = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_records_action_evidence", x => x.id);
                    table.UniqueConstraint("AK_records_action_evidence_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_records_action_evidence_values", "sequence >= 1 AND event_kind IN ('REQUESTED','OBSERVED','FAILED') AND length(trim(desired_label)) > 0 AND length(trim(desired_protection)) > 0 AND ((event_kind = 'OBSERVED' AND length(trim(observed_label)) > 0 AND length(trim(observed_protection)) > 0 AND length(trim(observed_by)) > 0) OR (event_kind = 'FAILED' AND length(trim(exception)) > 0) OR event_kind = 'REQUESTED')");
                    table.ForeignKey(
                        name: "FK_records_action_evidence_archive_manifests_firm_id_client_id~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.archive_manifest_id },
                        principalTable: "archive_manifests",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_records_action_evidence_archives_firm_id_client_id_engageme~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.archive_id },
                        principalTable: "archives",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_records_action_evidence_engagements_firm_id_client_id_engag~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_records_action_evidence_records_actions_firm_id_client_id_e~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.records_action_id },
                        principalTable: "records_actions",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_records_action_evidence_users_firm_id_actor_user_id",
                        columns: x => new { x.firm_id, x.actor_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_records_action_evidence_firm_id_actor_user_id",
                table: "records_action_evidence",
                columns: new[] { "firm_id", "actor_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_records_action_evidence_firm_id_client_id_engagement_id_ar~1",
                table: "records_action_evidence",
                columns: new[] { "firm_id", "client_id", "engagement_id", "archive_manifest_id" });

            migrationBuilder.CreateIndex(
                name: "IX_records_action_evidence_firm_id_client_id_engagement_id_arc~",
                table: "records_action_evidence",
                columns: new[] { "firm_id", "client_id", "engagement_id", "archive_id" });

            migrationBuilder.CreateIndex(
                name: "IX_records_action_evidence_firm_id_client_id_engagement_id_rec~",
                table: "records_action_evidence",
                columns: new[] { "firm_id", "client_id", "engagement_id", "records_action_id" });

            migrationBuilder.CreateIndex(
                name: "ux_records_action_evidence_sequence",
                table: "records_action_evidence",
                columns: new[] { "firm_id", "records_action_id", "sequence" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION enforce_records_action_evidence_append_only() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Records action evidence is append-only.' USING ERRCODE = '55000';
                END;
                $$;
                CREATE TRIGGER trg_records_action_evidence_append_only
                BEFORE UPDATE OR DELETE ON records_action_evidence
                FOR EACH ROW EXECUTE FUNCTION enforce_records_action_evidence_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM records_action_evidence) THEN
                        RAISE EXCEPTION 'Records action evidence downgrade would discard immutable evidence.';
                    END IF;
                END $$;
                DROP TRIGGER IF EXISTS trg_records_action_evidence_append_only ON records_action_evidence;
                DROP FUNCTION IF EXISTS enforce_records_action_evidence_append_only();
                """);
            migrationBuilder.DropTable(
                name: "records_action_evidence");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_records_actions_firm_id_id",
                table: "records_actions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_records_actions_scope_id",
                table: "records_actions");
        }
    }
}
