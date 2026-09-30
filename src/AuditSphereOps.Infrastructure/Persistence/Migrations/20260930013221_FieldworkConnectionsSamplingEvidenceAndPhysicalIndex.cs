using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FieldworkConnectionsSamplingEvidenceAndPhysicalIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ad_hoc_procedure_insertions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_program_id = table.Column<Guid>(type: "uuid", nullable: false),
                    baseline_program_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    risk_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: false),
                    inserted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inserted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ad_hoc_procedure_insertions", x => x.id);
                    table.CheckConstraint("ck_ad_hoc_procedure_insertion_values", "length(reason) > 0");
                    table.ForeignKey(
                        name: "FK_ad_hoc_procedure_insertions_audit_procedures_procedure_id",
                        column: x => x.procedure_id,
                        principalTable: "audit_procedures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ad_hoc_procedure_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    wording = table.Column<string>(type: "text", nullable: false),
                    edited_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    edited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ad_hoc_procedure_revisions", x => x.id);
                    table.CheckConstraint("ck_ad_hoc_procedure_revision_values", "revision >= 1 AND length(wording) > 0");
                    table.ForeignKey(
                        name: "FK_ad_hoc_procedure_revisions_audit_procedures_procedure_id",
                        column: x => x.procedure_id,
                        principalTable: "audit_procedures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_sampling_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    selection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_id = table.Column<Guid>(type: "uuid", nullable: false),
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method = table.Column<string>(type: "text", nullable: false),
                    interval = table.Column<decimal>(type: "numeric(19,6)", precision: 28, scale: 6, nullable: true),
                    key_item_threshold = table.Column<decimal>(type: "numeric(19,6)", precision: 28, scale: 6, nullable: true),
                    sample_size = table.Column<int>(type: "integer", nullable: true),
                    seed = table.Column<int>(type: "integer", nullable: true),
                    population_count = table.Column<int>(type: "integer", nullable: false),
                    population_absolute_total = table.Column<decimal>(type: "numeric(19,6)", precision: 28, scale: 6, nullable: false),
                    selected_count = table.Column<int>(type: "integer", nullable: false),
                    selected_absolute_total = table.Column<decimal>(type: "numeric(19,6)", precision: 28, scale: 6, nullable: false),
                    coverage_percent = table.Column<decimal>(type: "numeric(19,6)", precision: 9, scale: 4, nullable: false),
                    source_digest = table.Column<string>(type: "text", nullable: false),
                    selection_digest = table.Column<string>(type: "text", nullable: false),
                    engine_version = table.Column<string>(type: "text", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_sampling_runs", x => x.id);
                    table.CheckConstraint("ck_audit_sampling_run_values", "method IN ('MUS','KEY_ITEM','RANDOM','STRATIFIED') AND selected_count > 0 AND selected_count <= population_count AND length(source_digest) = 64 AND length(selection_digest) = 64");
                    table.ForeignKey(
                        name: "FK_audit_sampling_runs_audit_selections_selection_id",
                        column: x => x.selection_id,
                        principalTable: "audit_selections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "physical_evidence_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_index = table.Column<string>(type: "text", nullable: false),
                    box_reference = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    current_location = table.Column<string>(type: "text", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_physical_evidence_items", x => x.id);
                    table.CheckConstraint("ck_physical_evidence_item_values", "length(file_index) BETWEEN 1 AND 40 AND length(box_reference) BETWEEN 1 AND 40 AND length(current_location) > 0");
                });

            migrationBuilder.CreateTable(
                name: "procedure_evidence_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pbc_upload_intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pbc_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: false),
                    content_sha256 = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    linked_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_procedure_evidence_links", x => x.id);
                    table.CheckConstraint("ck_procedure_evidence_link_values", "length(content_sha256) = 64");
                    table.ForeignKey(
                        name: "FK_procedure_evidence_links_audit_procedures_procedure_id",
                        column: x => x.procedure_id,
                        principalTable: "audit_procedures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_procedure_evidence_links_pbc_upload_intents_pbc_upload_inte~",
                        column: x => x.pbc_upload_intent_id,
                        principalTable: "pbc_upload_intents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "physical_evidence_movements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    physical_evidence_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_location = table.Column<string>(type: "text", nullable: true),
                    to_location = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    moved_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    moved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_physical_evidence_movements", x => x.id);
                    table.CheckConstraint("ck_physical_evidence_movement_values", "length(to_location) > 0 AND length(reason) > 0");
                    table.ForeignKey(
                        name: "FK_physical_evidence_movements_physical_evidence_items_physica~",
                        column: x => x.physical_evidence_item_id,
                        principalTable: "physical_evidence_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "procedure_physical_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_id = table.Column<Guid>(type: "uuid", nullable: false),
                    physical_evidence_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    linked_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_procedure_physical_links", x => x.id);
                    table.ForeignKey(
                        name: "FK_procedure_physical_links_audit_procedures_procedure_id",
                        column: x => x.procedure_id,
                        principalTable: "audit_procedures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_procedure_physical_links_physical_evidence_items_physical_e~",
                        column: x => x.physical_evidence_item_id,
                        principalTable: "physical_evidence_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ad_hoc_procedure_insertions_firm_id_procedure_id",
                table: "ad_hoc_procedure_insertions",
                columns: new[] { "firm_id", "procedure_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ad_hoc_procedure_insertions_procedure_id",
                table: "ad_hoc_procedure_insertions",
                column: "procedure_id");

            migrationBuilder.CreateIndex(
                name: "IX_ad_hoc_procedure_revisions_firm_id_procedure_id_revision",
                table: "ad_hoc_procedure_revisions",
                columns: new[] { "firm_id", "procedure_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ad_hoc_procedure_revisions_procedure_id",
                table: "ad_hoc_procedure_revisions",
                column: "procedure_id");

            migrationBuilder.CreateIndex(
                name: "IX_audit_sampling_runs_firm_id_engagement_id_created_at",
                table: "audit_sampling_runs",
                columns: new[] { "firm_id", "engagement_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_sampling_runs_firm_id_selection_id",
                table: "audit_sampling_runs",
                columns: new[] { "firm_id", "selection_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_sampling_runs_selection_id",
                table: "audit_sampling_runs",
                column: "selection_id");

            migrationBuilder.CreateIndex(
                name: "IX_physical_evidence_items_firm_id_engagement_id_file_index",
                table: "physical_evidence_items",
                columns: new[] { "firm_id", "engagement_id", "file_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_physical_evidence_movements_firm_id_physical_evidence_item_~",
                table: "physical_evidence_movements",
                columns: new[] { "firm_id", "physical_evidence_item_id", "moved_at" });

            migrationBuilder.CreateIndex(
                name: "IX_physical_evidence_movements_physical_evidence_item_id",
                table: "physical_evidence_movements",
                column: "physical_evidence_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_procedure_evidence_links_firm_id_procedure_id_pbc_upload_in~",
                table: "procedure_evidence_links",
                columns: new[] { "firm_id", "procedure_id", "pbc_upload_intent_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_procedure_evidence_links_pbc_upload_intent_id",
                table: "procedure_evidence_links",
                column: "pbc_upload_intent_id");

            migrationBuilder.CreateIndex(
                name: "IX_procedure_evidence_links_procedure_id",
                table: "procedure_evidence_links",
                column: "procedure_id");

            migrationBuilder.CreateIndex(
                name: "IX_procedure_physical_links_firm_id_procedure_id_physical_evid~",
                table: "procedure_physical_links",
                columns: new[] { "firm_id", "procedure_id", "physical_evidence_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_procedure_physical_links_physical_evidence_item_id",
                table: "procedure_physical_links",
                column: "physical_evidence_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_procedure_physical_links_procedure_id",
                table: "procedure_physical_links",
                column: "procedure_id");
            migrationBuilder.Sql("""
                CREATE FUNCTION prevent_fieldwork_evidence_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION '% rows are append-only fieldwork evidence', TG_TABLE_NAME;
                END;
                $$;
                CREATE TRIGGER trg_audit_sampling_runs_append_only BEFORE UPDATE OR DELETE ON audit_sampling_runs FOR EACH ROW EXECUTE FUNCTION prevent_fieldwork_evidence_mutation();
                CREATE TRIGGER trg_procedure_evidence_links_append_only BEFORE UPDATE OR DELETE ON procedure_evidence_links FOR EACH ROW EXECUTE FUNCTION prevent_fieldwork_evidence_mutation();
                CREATE TRIGGER trg_physical_evidence_movements_append_only BEFORE UPDATE OR DELETE ON physical_evidence_movements FOR EACH ROW EXECUTE FUNCTION prevent_fieldwork_evidence_mutation();
                CREATE TRIGGER trg_ad_hoc_procedure_insertions_append_only BEFORE UPDATE OR DELETE ON ad_hoc_procedure_insertions FOR EACH ROW EXECUTE FUNCTION prevent_fieldwork_evidence_mutation();
                CREATE TRIGGER trg_ad_hoc_procedure_revisions_append_only BEFORE UPDATE OR DELETE ON ad_hoc_procedure_revisions FOR EACH ROW EXECUTE FUNCTION prevent_fieldwork_evidence_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_audit_sampling_runs_append_only ON audit_sampling_runs;
                DROP TRIGGER IF EXISTS trg_procedure_evidence_links_append_only ON procedure_evidence_links;
                DROP TRIGGER IF EXISTS trg_physical_evidence_movements_append_only ON physical_evidence_movements;
                DROP TRIGGER IF EXISTS trg_ad_hoc_procedure_insertions_append_only ON ad_hoc_procedure_insertions;
                DROP TRIGGER IF EXISTS trg_ad_hoc_procedure_revisions_append_only ON ad_hoc_procedure_revisions;
                DROP FUNCTION IF EXISTS prevent_fieldwork_evidence_mutation();
                """);

            migrationBuilder.DropTable(
                name: "ad_hoc_procedure_insertions");

            migrationBuilder.DropTable(
                name: "ad_hoc_procedure_revisions");

            migrationBuilder.DropTable(
                name: "audit_sampling_runs");

            migrationBuilder.DropTable(
                name: "physical_evidence_movements");

            migrationBuilder.DropTable(
                name: "procedure_evidence_links");

            migrationBuilder.DropTable(
                name: "procedure_physical_links");

            migrationBuilder.DropTable(
                name: "physical_evidence_items");
        }
    }
}
