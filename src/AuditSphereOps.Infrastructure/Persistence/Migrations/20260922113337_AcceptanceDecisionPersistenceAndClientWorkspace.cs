using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AcceptanceDecisionPersistenceAndClientWorkspace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_acceptance_decision_values",
                table: "acceptance_decisions");

            migrationBuilder.AddColumn<string>(
                name: "conditions",
                table: "acceptance_decisions",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "evaluation_snapshot_digest",
                table: "acceptance_decisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "evaluation_template_version",
                table: "acceptance_decisions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "rationale",
                table: "acceptance_decisions",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_acceptance_decisions_firm_id_id",
                table: "acceptance_decisions",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.CreateTable(
                name: "client_workspaces",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    practice_client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    acceptance_decision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    logical_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    state = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    connection_revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    folder_template_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    site_id = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    drive_id = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    root_folder_id = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    remote_item_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    last_error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_workspaces", x => x.id);
                    table.UniqueConstraint("AK_client_workspaces_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_client_workspace_values", "purpose = 'PRIMARY' AND length(trim(logical_key)) > 0 AND state IN ('WAITING_FOR_INTEGRATION','QUEUED','PROVISIONING','VERIFYING','READY','BLOCKED_ACCEPTANCE','BLOCKED_CONFIGURATION','RESULT_UNCERTAIN','CONFLICT_REQUIRES_REVIEW','SUSPENDED') AND revision >= 1");
                    table.ForeignKey(
                        name: "FK_client_workspaces_acceptance_decisions_firm_id_acceptance_d~",
                        columns: x => new { x.firm_id, x.acceptance_decision_id },
                        principalTable: "acceptance_decisions",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_workspaces_m365_connection_revisions_firm_id_connect~",
                        columns: x => new { x.firm_id, x.connection_revision_id },
                        principalTable: "m365_connection_revisions",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_workspaces_m365_folder_template_versions_firm_id_fol~",
                        columns: x => new { x.firm_id, x.folder_template_version_id },
                        principalTable: "m365_folder_template_versions",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_workspaces_practice_clients_firm_id_practice_client_~",
                        columns: x => new { x.firm_id, x.practice_client_id },
                        principalTable: "practice_clients",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_acceptance_decision_evidence",
                table: "acceptance_decisions",
                sql: "((decision = 'Pending') OR (length(trim(rationale)) > 0 AND length(trim(evaluation_template_version)) > 0 AND evaluation_snapshot_digest ~ '^[0-9a-f]{64}$')) AND ((decision = 'AcceptedWithConditions' AND length(trim(conditions)) > 0) OR decision <> 'AcceptedWithConditions')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_acceptance_decision_values",
                table: "acceptance_decisions",
                sql: "generation >= 1 AND length(trim(service_route)) > 0 AND decision IN ('Pending','Accepted','AcceptedWithConditions','Declined','Deferred') AND ((decision IN ('Accepted','AcceptedWithConditions','Declined','Deferred') AND decided_at IS NOT NULL        AND decided_by_user_id IS NOT NULL) OR decision = 'Pending')");

            migrationBuilder.CreateIndex(
                name: "IX_client_workspaces_firm_id_acceptance_decision_id",
                table: "client_workspaces",
                columns: new[] { "firm_id", "acceptance_decision_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_workspaces_firm_id_connection_revision_id",
                table: "client_workspaces",
                columns: new[] { "firm_id", "connection_revision_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_workspaces_firm_id_folder_template_version_id",
                table: "client_workspaces",
                columns: new[] { "firm_id", "folder_template_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_workspaces_firm_id_logical_key",
                table: "client_workspaces",
                columns: new[] { "firm_id", "logical_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_workspaces_firm_id_practice_client_id_purpose",
                table: "client_workspaces",
                columns: new[] { "firm_id", "practice_client_id", "purpose" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE FUNCTION protect_acceptance_decision() RETURNS trigger AS $$
                BEGIN
                  RAISE EXCEPTION 'Acceptance decisions are append-only.' USING ERRCODE = '55000';
                END; $$ LANGUAGE plpgsql;
                CREATE TRIGGER trg_acceptance_decision_history
                  BEFORE UPDATE OR DELETE ON acceptance_decisions
                  FOR EACH ROW EXECUTE FUNCTION protect_acceptance_decision();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS protect_acceptance_decision() CASCADE;");
            migrationBuilder.DropTable(
                name: "client_workspaces");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_acceptance_decisions_firm_id_id",
                table: "acceptance_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_acceptance_decision_evidence",
                table: "acceptance_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_acceptance_decision_values",
                table: "acceptance_decisions");

            migrationBuilder.DropColumn(
                name: "conditions",
                table: "acceptance_decisions");

            migrationBuilder.DropColumn(
                name: "evaluation_snapshot_digest",
                table: "acceptance_decisions");

            migrationBuilder.DropColumn(
                name: "evaluation_template_version",
                table: "acceptance_decisions");

            migrationBuilder.DropColumn(
                name: "rationale",
                table: "acceptance_decisions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_acceptance_decision_values",
                table: "acceptance_decisions",
                sql: "generation >= 1 AND length(trim(service_route)) > 0 AND decision IN ('Pending','Accepted','AcceptedWithConditions','Declined') AND ((decision IN ('Accepted','AcceptedWithConditions','Declined') AND decided_at IS NOT NULL        AND decided_by_user_id IS NOT NULL) OR decision = 'Pending')");
        }
    }
}
