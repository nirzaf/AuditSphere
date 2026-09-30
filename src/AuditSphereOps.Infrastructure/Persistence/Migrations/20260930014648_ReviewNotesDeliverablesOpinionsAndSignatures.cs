using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReviewNotesDeliverablesOpinionsAndSignatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "dispatched_at",
                table: "audit_confirmation_cases",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "audit_deliverables",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    template_version = table.Column<string>(type: "text", nullable: false),
                    input_digest = table.Column<string>(type: "text", nullable: false),
                    input_summary_json = table.Column<string>(type: "jsonb", nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    content_sha256 = table.Column<string>(type: "text", nullable: false),
                    signed_from_deliverable_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_deliverables", x => x.id);
                    table.CheckConstraint("ck_audit_deliverable_values", "kind IN ('SUMMARY_REVIEW_MEMORANDUM','AUDIT_FINDINGS_REPORT','MANAGEMENT_LETTER','INDEPENDENT_AUDITORS_REPORT','HOLDING_LETTER','REPRESENTATION_LETTER') AND version >= 1 AND length(input_digest) = 64 AND length(content_sha256) = 64 AND octet_length(content) > 0");
                });

            migrationBuilder.CreateTable(
                name: "confirmation_criticalities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    confirmation_case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    critical = table.Column<bool>(type: "boolean", nullable: false),
                    rationale = table.Column<string>(type: "text", nullable: false),
                    set_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    set_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_confirmation_criticalities", x => x.id);
                    table.CheckConstraint("ck_confirmation_criticality_values", "length(rationale) > 0");
                    table.ForeignKey(
                        name: "FK_confirmation_criticalities_audit_confirmation_cases_confirm~",
                        column: x => x.confirmation_case_id,
                        principalTable: "audit_confirmation_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "procedure_review_notes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_id = table.Column<Guid>(type: "uuid", nullable: false),
                    result_id = table.Column<Guid>(type: "uuid", nullable: false),
                    result_revision = table.Column<long>(type: "bigint", nullable: false),
                    field = table.Column<string>(type: "text", nullable: false),
                    excerpt = table.Column<string>(type: "text", nullable: false),
                    start_offset = table.Column<int>(type: "integer", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_procedure_review_notes", x => x.id);
                    table.CheckConstraint("ck_procedure_review_note_values", "field IN ('WORK_PERFORMED','CONCLUSION') AND length(excerpt) BETWEEN 1 AND 500 AND length(body) BETWEEN 1 AND 4000 AND start_offset >= 0");
                    table.ForeignKey(
                        name: "FK_procedure_review_notes_audit_procedure_results_result_id",
                        column: x => x.result_id,
                        principalTable: "audit_procedure_results",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "signature_specimens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    png_content = table.Column<byte[]>(type: "bytea", nullable: false),
                    sha256 = table.Column<string>(type: "text", nullable: false),
                    width_pixels = table.Column<int>(type: "integer", nullable: false),
                    height_pixels = table.Column<int>(type: "integer", nullable: false),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_signature_specimens", x => x.id);
                    table.CheckConstraint("ck_signature_specimen_values", "length(sha256) = 64 AND octet_length(png_content) BETWEEN 1 AND 524288 AND width_pixels > 0 AND height_pixels > 0");
                });

            migrationBuilder.CreateTable(
                name: "client_deliverable_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deliverable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shared_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shared_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    acknowledged_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    acknowledged_sha256 = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_deliverable_reviews", x => x.id);
                    table.CheckConstraint("ck_client_deliverable_review_values", "(acknowledged_at IS NULL) = (acknowledged_sha256 IS NULL)");
                    table.ForeignKey(
                        name: "FK_client_deliverable_reviews_audit_deliverables_deliverable_id",
                        column: x => x.deliverable_id,
                        principalTable: "audit_deliverables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "partner_completion_clearances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    summary_review_memorandum_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_risk_areas_comment = table.Column<string>(type: "text", nullable: false),
                    financial_statement_notes_comment = table.Column<string>(type: "text", nullable: false),
                    partner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cleared_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_partner_completion_clearances", x => x.id);
                    table.CheckConstraint("ck_partner_completion_clearance_values", "length(key_risk_areas_comment) > 0 AND length(financial_statement_notes_comment) > 0");
                    table.ForeignKey(
                        name: "FK_partner_completion_clearances_audit_deliverables_summary_re~",
                        column: x => x.summary_review_memorandum_id,
                        principalTable: "audit_deliverables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "procedure_review_note_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_procedure_review_note_events", x => x.id);
                    table.CheckConstraint("ck_procedure_review_note_event_values", "kind IN ('RESPONSE','RESOLVED','REOPENED') AND length(body) > 0");
                    table.ForeignKey(
                        name: "FK_procedure_review_note_events_procedure_review_notes_note_id",
                        column: x => x.note_id,
                        principalTable: "procedure_review_notes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "signature_applications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    specimen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_deliverable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    signed_deliverable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    signed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    signed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_signature_applications", x => x.id);
                    table.ForeignKey(
                        name: "FK_signature_applications_signature_specimens_specimen_id",
                        column: x => x.specimen_id,
                        principalTable: "signature_specimens",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "client_deliverable_comments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_client = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolution = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_deliverable_comments", x => x.id);
                    table.CheckConstraint("ck_client_deliverable_comment_values", "length(body) > 0 AND ((resolved_at IS NULL) = (resolution IS NULL))");
                    table.ForeignKey(
                        name: "FK_client_deliverable_comments_client_deliverable_reviews_revi~",
                        column: x => x.review_id,
                        principalTable: "client_deliverable_reviews",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_opinion_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    partner_clearance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opinion_type = table.Column<string>(type: "text", nullable: false),
                    focus_area = table.Column<string>(type: "text", nullable: true),
                    basis_text = table.Column<string>(type: "text", nullable: true),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_opinion_decisions", x => x.id);
                    table.CheckConstraint("ck_audit_opinion_decision_values", "opinion_type IN ('UNMODIFIED','QUALIFIED','ADVERSE','DISCLAIMER') AND ((opinion_type = 'UNMODIFIED' AND basis_text IS NULL) OR (opinion_type <> 'UNMODIFIED' AND length(basis_text) > 0)) AND (opinion_type <> 'QUALIFIED' OR length(focus_area) > 0)");
                    table.ForeignKey(
                        name: "FK_audit_opinion_decisions_partner_completion_clearances_partn~",
                        column: x => x.partner_clearance_id,
                        principalTable: "partner_completion_clearances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_deliverables_firm_id_engagement_id_kind_version",
                table: "audit_deliverables",
                columns: new[] { "firm_id", "engagement_id", "kind", "version" },
                unique: true,
                filter: "signed_from_deliverable_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_audit_deliverables_firm_id_signed_from_deliverable_id",
                table: "audit_deliverables",
                columns: new[] { "firm_id", "signed_from_deliverable_id" },
                unique: true,
                filter: "signed_from_deliverable_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_audit_opinion_decisions_firm_id_engagement_id_decided_at",
                table: "audit_opinion_decisions",
                columns: new[] { "firm_id", "engagement_id", "decided_at" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_opinion_decisions_partner_clearance_id",
                table: "audit_opinion_decisions",
                column: "partner_clearance_id");

            migrationBuilder.CreateIndex(
                name: "IX_client_deliverable_comments_firm_id_review_id_created_at",
                table: "client_deliverable_comments",
                columns: new[] { "firm_id", "review_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_client_deliverable_comments_review_id",
                table: "client_deliverable_comments",
                column: "review_id");

            migrationBuilder.CreateIndex(
                name: "IX_client_deliverable_reviews_deliverable_id",
                table: "client_deliverable_reviews",
                column: "deliverable_id");

            migrationBuilder.CreateIndex(
                name: "IX_client_deliverable_reviews_firm_id_deliverable_id",
                table: "client_deliverable_reviews",
                columns: new[] { "firm_id", "deliverable_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_confirmation_criticalities_confirmation_case_id",
                table: "confirmation_criticalities",
                column: "confirmation_case_id");

            migrationBuilder.CreateIndex(
                name: "IX_confirmation_criticalities_firm_id_confirmation_case_id_set~",
                table: "confirmation_criticalities",
                columns: new[] { "firm_id", "confirmation_case_id", "set_at" });

            migrationBuilder.CreateIndex(
                name: "IX_partner_completion_clearances_firm_id_engagement_id_cleared~",
                table: "partner_completion_clearances",
                columns: new[] { "firm_id", "engagement_id", "cleared_at" });

            migrationBuilder.CreateIndex(
                name: "IX_partner_completion_clearances_summary_review_memorandum_id",
                table: "partner_completion_clearances",
                column: "summary_review_memorandum_id");

            migrationBuilder.CreateIndex(
                name: "IX_procedure_review_note_events_firm_id_note_id_created_at",
                table: "procedure_review_note_events",
                columns: new[] { "firm_id", "note_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_procedure_review_note_events_note_id",
                table: "procedure_review_note_events",
                column: "note_id");

            migrationBuilder.CreateIndex(
                name: "IX_procedure_review_notes_firm_id_procedure_id_created_at",
                table: "procedure_review_notes",
                columns: new[] { "firm_id", "procedure_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_procedure_review_notes_result_id",
                table: "procedure_review_notes",
                column: "result_id");

            migrationBuilder.CreateIndex(
                name: "IX_signature_applications_firm_id_source_deliverable_id",
                table: "signature_applications",
                columns: new[] { "firm_id", "source_deliverable_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_signature_applications_specimen_id",
                table: "signature_applications",
                column: "specimen_id");

            migrationBuilder.CreateIndex(
                name: "IX_signature_specimens_firm_id_user_id",
                table: "signature_specimens",
                columns: new[] { "firm_id", "user_id" },
                unique: true,
                filter: "revoked_at IS NULL");
            migrationBuilder.Sql("""
                CREATE FUNCTION prevent_completion_evidence_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION '% rows are append-only completion evidence', TG_TABLE_NAME;
                END;
                $$;
                CREATE TRIGGER trg_procedure_review_notes_append_only BEFORE UPDATE OR DELETE ON procedure_review_notes FOR EACH ROW EXECUTE FUNCTION prevent_completion_evidence_mutation();
                CREATE TRIGGER trg_procedure_review_note_events_append_only BEFORE UPDATE OR DELETE ON procedure_review_note_events FOR EACH ROW EXECUTE FUNCTION prevent_completion_evidence_mutation();
                CREATE TRIGGER trg_audit_deliverables_append_only BEFORE UPDATE OR DELETE ON audit_deliverables FOR EACH ROW EXECUTE FUNCTION prevent_completion_evidence_mutation();
                CREATE TRIGGER trg_partner_completion_clearances_append_only BEFORE UPDATE OR DELETE ON partner_completion_clearances FOR EACH ROW EXECUTE FUNCTION prevent_completion_evidence_mutation();
                CREATE TRIGGER trg_audit_opinion_decisions_append_only BEFORE UPDATE OR DELETE ON audit_opinion_decisions FOR EACH ROW EXECUTE FUNCTION prevent_completion_evidence_mutation();
                CREATE TRIGGER trg_signature_applications_append_only BEFORE UPDATE OR DELETE ON signature_applications FOR EACH ROW EXECUTE FUNCTION prevent_completion_evidence_mutation();
                CREATE TRIGGER trg_confirmation_criticalities_append_only BEFORE UPDATE OR DELETE ON confirmation_criticalities FOR EACH ROW EXECUTE FUNCTION prevent_completion_evidence_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_procedure_review_notes_append_only ON procedure_review_notes;
                DROP TRIGGER IF EXISTS trg_procedure_review_note_events_append_only ON procedure_review_note_events;
                DROP TRIGGER IF EXISTS trg_audit_deliverables_append_only ON audit_deliverables;
                DROP TRIGGER IF EXISTS trg_partner_completion_clearances_append_only ON partner_completion_clearances;
                DROP TRIGGER IF EXISTS trg_audit_opinion_decisions_append_only ON audit_opinion_decisions;
                DROP TRIGGER IF EXISTS trg_signature_applications_append_only ON signature_applications;
                DROP TRIGGER IF EXISTS trg_confirmation_criticalities_append_only ON confirmation_criticalities;
                DROP FUNCTION IF EXISTS prevent_completion_evidence_mutation();
                """);

            migrationBuilder.DropTable(
                name: "audit_opinion_decisions");

            migrationBuilder.DropTable(
                name: "client_deliverable_comments");

            migrationBuilder.DropTable(
                name: "confirmation_criticalities");

            migrationBuilder.DropTable(
                name: "procedure_review_note_events");

            migrationBuilder.DropTable(
                name: "signature_applications");

            migrationBuilder.DropTable(
                name: "partner_completion_clearances");

            migrationBuilder.DropTable(
                name: "client_deliverable_reviews");

            migrationBuilder.DropTable(
                name: "procedure_review_notes");

            migrationBuilder.DropTable(
                name: "signature_specimens");

            migrationBuilder.DropTable(
                name: "audit_deliverables");

            migrationBuilder.DropColumn(
                name: "dispatched_at",
                table: "audit_confirmation_cases");
        }
    }
}
