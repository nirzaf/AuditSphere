using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LifecycleAuditDeliverableAndOffboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_offboarding_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    readiness_overall_state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    readiness_snapshot_json = table.Column<string>(type: "text", nullable: false),
                    readiness_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    disposition_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    acknowledged_action_items_json = table.Column<string>(type: "text", nullable: false),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_offboarding_records", x => x.id);
                    table.UniqueConstraint("ak_client_offboarding_records_client_id", x => new { x.firm_id, x.client_id, x.id });
                    table.CheckConstraint("ck_client_offboarding_record_values", "length(trim(status)) > 0 AND length(trim(disposition_notes)) > 0 AND readiness_digest ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_client_offboarding_records_practice_clients_firm_id_client_~",
                        columns: x => new { x.firm_id, x.client_id },
                        principalTable: "practice_clients",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_offboarding_records_users_firm_id_decided_by_user_id",
                        columns: x => new { x.firm_id, x.decided_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "group_deliverables",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    input_manifest_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    deliverable_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    deliverable_json = table.Column<string>(type: "text", nullable: false),
                    format_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    issued_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    authorized_release_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    archived_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    archive_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_deliverables", x => x.id);
                    table.UniqueConstraint("ak_group_deliverables_scope_id", x => new { x.firm_id, x.group_id, x.scope_version_id, x.id });
                    table.CheckConstraint("ck_group_deliverable_values", "format_version <> '' AND run_hash ~ '^[0-9a-f]{64}$' AND input_manifest_digest ~ '^[0-9a-f]{64}$' AND deliverable_sha256 ~ '^[0-9a-f]{64}$' AND status IN ('BUILT','ISSUED','ARCHIVED')");
                    table.ForeignKey(
                        name: "FK_group_deliverables_client_groups_firm_id_group_id",
                        columns: x => new { x.firm_id, x.group_id },
                        principalTable: "client_groups",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_group_deliverables_consolidation_runs_firm_id_group_id_scop~",
                        columns: x => new { x.firm_id, x.group_id, x.scope_version_id, x.run_id },
                        principalTable: "consolidation_runs",
                        principalColumns: new[] { "firm_id", "group_id", "scope_version_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_group_deliverables_consolidation_scope_versions_firm_id_gro~",
                        columns: x => new { x.firm_id, x.group_id, x.scope_version_id },
                        principalTable: "consolidation_scope_versions",
                        principalColumns: new[] { "firm_id", "group_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_group_deliverables_users_firm_id_archived_by_user_id",
                        columns: x => new { x.firm_id, x.archived_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_group_deliverables_users_firm_id_created_by_user_id",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_group_deliverables_users_firm_id_issued_by_user_id",
                        columns: x => new { x.firm_id, x.issued_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_offboarding_records_firm_id_client_id_decided_at",
                table: "client_offboarding_records",
                columns: new[] { "firm_id", "client_id", "decided_at" });

            migrationBuilder.CreateIndex(
                name: "IX_client_offboarding_records_firm_id_decided_by_user_id",
                table: "client_offboarding_records",
                columns: new[] { "firm_id", "decided_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_group_deliverables_firm_id_archived_by_user_id",
                table: "group_deliverables",
                columns: new[] { "firm_id", "archived_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_group_deliverables_firm_id_created_by_user_id",
                table: "group_deliverables",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_group_deliverables_firm_id_group_id_scope_version_id_run_id",
                table: "group_deliverables",
                columns: new[] { "firm_id", "group_id", "scope_version_id", "run_id" });

            migrationBuilder.CreateIndex(
                name: "IX_group_deliverables_firm_id_issued_by_user_id",
                table: "group_deliverables",
                columns: new[] { "firm_id", "issued_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_group_deliverables_scope_run_status",
                table: "group_deliverables",
                columns: new[] { "firm_id", "scope_version_id", "run_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "client_offboarding_records");

            migrationBuilder.DropTable(
                name: "group_deliverables");
        }
    }
}
