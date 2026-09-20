using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditProgramExecutionWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_procedure_values",
                table: "audit_procedures");

            migrationBuilder.AlterColumn<Guid>(
                name: "risk_id",
                table: "audit_procedures",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "applicability_decided_at",
                table: "audit_procedures",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "applicability_decided_by_user_id",
                table: "audit_procedures",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "applicability_rationale",
                table: "audit_procedures",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "applicability_status",
                table: "audit_procedures",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "current_result_revision",
                table: "audit_procedures",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "engagement_program_id",
                table: "audit_procedures",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "program_procedure_id",
                table: "audit_procedures",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_procedure_id",
                table: "audit_procedures",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "source_section_number",
                table: "audit_procedures",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_section_title",
                table: "audit_procedures",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_wording",
                table: "audit_procedures",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "audit_procedure_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audit_procedure_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workpaper_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    input_generation = table.Column<long>(type: "bigint", nullable: false),
                    work_performed = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    structured_result_json = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    evidence_references_json = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    conclusion = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    prepared_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    review_comment = table.Column<string>(type: "text", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_procedure_results", x => x.id);
                    table.UniqueConstraint("AK_audit_procedure_results_firm_id_client_id_engagement_id_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.UniqueConstraint("AK_audit_procedure_results_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_audit_procedure_result_values", "revision > 0 AND input_generation > 0 AND length(trim(work_performed)) > 0 AND length(trim(structured_result_json)) > 0 AND length(trim(conclusion)) > 0 AND status IN ('SUBMITTED','CHANGES_REQUIRED','REVIEWED') AND ((status = 'REVIEWED' AND reviewed_by_user_id IS NOT NULL AND reviewed_at IS NOT NULL) OR status <> 'REVIEWED')");
                    table.ForeignKey(
                        name: "FK_audit_procedure_results_audit_procedures_firm_id_client_id_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.audit_procedure_id },
                        principalTable: "audit_procedures",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_procedure_results_engagements_firm_id_client_id_engag~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_procedure_results_users_firm_id_prepared_by_user_id",
                        columns: x => new { x.firm_id, x.prepared_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_procedure_results_users_firm_id_reviewed_by_user_id",
                        columns: x => new { x.firm_id, x.reviewed_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_procedure_results_workpapers_firm_id_client_id_engage~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.workpaper_id },
                        principalTable: "workpapers",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_program_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    program_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_program_versions", x => x.id);
                    table.UniqueConstraint("AK_audit_program_versions_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_audit_program_version_values", "length(trim(program_code)) > 0 AND length(trim(version)) > 0 AND length(source_hash) = 64 AND status IN ('DRAFT','PUBLISHED','RETIRED') AND ((status = 'PUBLISHED' AND approved_by_user_id IS NOT NULL AND approved_at IS NOT NULL) OR status <> 'PUBLISHED')");
                    table.ForeignKey(
                        name: "FK_audit_program_versions_users_firm_id_approved_by_user_id",
                        columns: x => new { x.firm_id, x.approved_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_program_versions_users_firm_id_created_by_user_id",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_procedure_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audit_procedure_result_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audit_procedure_id = table.Column<Guid>(type: "uuid", nullable: false),
                    result_revision = table.Column<long>(type: "bigint", nullable: false),
                    decision = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    comment = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    reviewer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_procedure_reviews", x => x.id);
                    table.UniqueConstraint("AK_audit_procedure_reviews_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_audit_procedure_review_values", "result_revision > 0 AND decision IN ('REVIEWED','CHANGES_REQUIRED')");
                    table.ForeignKey(
                        name: "FK_audit_procedure_reviews_audit_procedure_results_firm_id_cli~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.audit_procedure_result_id },
                        principalTable: "audit_procedure_results",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_procedure_reviews_audit_procedures_firm_id_client_id_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.audit_procedure_id },
                        principalTable: "audit_procedures",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_procedure_reviews_engagements_firm_id_client_id_engag~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_procedure_reviews_users_firm_id_reviewer_user_id",
                        columns: x => new { x.firm_id, x.reviewer_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_program_procedures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    program_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_procedure_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    section_number = table.Column<int>(type: "integer", nullable: false),
                    section_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    source_wording = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    applicability_condition = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    expected_evidence = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_program_procedures", x => x.id);
                    table.UniqueConstraint("AK_audit_program_procedures_firm_id_id", x => new { x.firm_id, x.id });
                    table.UniqueConstraint("AK_audit_program_procedures_version_source", x => new { x.firm_id, x.program_version_id, x.source_procedure_id });
                    table.CheckConstraint("ck_audit_program_procedure_values", "section_number BETWEEN 1 AND 20 AND ordinal >= 1 AND length(trim(source_procedure_id)) > 0 AND length(trim(section_title)) > 0 AND length(trim(source_wording)) > 0");
                    table.ForeignKey(
                        name: "FK_audit_program_procedures_audit_program_versions_firm_id_pro~",
                        columns: x => new { x.firm_id, x.program_version_id },
                        principalTable: "audit_program_versions",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "engagement_audit_programs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    program_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    adopted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    adopted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_engagement_audit_programs", x => x.id);
                    table.UniqueConstraint("AK_engagement_audit_programs_firm_id_client_id_engagement_id_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.UniqueConstraint("AK_engagement_audit_programs_firm_id_id", x => new { x.firm_id, x.id });
                    table.UniqueConstraint("AK_engagement_audit_programs_scope_version", x => new { x.firm_id, x.client_id, x.engagement_id, x.program_version_id });
                    table.CheckConstraint("ck_engagement_audit_program_values", "status IN ('ADOPTED','SUPERSEDED')");
                    table.ForeignKey(
                        name: "FK_engagement_audit_programs_audit_program_versions_firm_id_pr~",
                        columns: x => new { x.firm_id, x.program_version_id },
                        principalTable: "audit_program_versions",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_engagement_audit_programs_engagements_firm_id_client_id_eng~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_engagement_audit_programs_users_firm_id_adopted_by_user_id",
                        columns: x => new { x.firm_id, x.adopted_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_procedures_firm_id_applicability_decided_by_user_id",
                table: "audit_procedures",
                columns: new[] { "firm_id", "applicability_decided_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_procedures_firm_id_client_id_engagement_id_engagement~",
                table: "audit_procedures",
                columns: new[] { "firm_id", "client_id", "engagement_id", "engagement_program_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_procedures_firm_id_program_procedure_id",
                table: "audit_procedures",
                columns: new[] { "firm_id", "program_procedure_id" });

            migrationBuilder.CreateIndex(
                name: "ux_audit_procedure_scope_source",
                table: "audit_procedures",
                columns: new[] { "firm_id", "client_id", "engagement_id", "source_procedure_id" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_procedure_values",
                table: "audit_procedures",
                sql: "length(trim(title)) > 0 AND status IN ('PLANNED','IN_PROGRESS','SUBMITTED','IN_REVIEW','CHANGES_REQUIRED','REVIEWED') AND applicability_status IN ('PENDING','APPLICABLE','NA_PENDING_REVIEW','NA_APPROVED')");

            migrationBuilder.CreateIndex(
                name: "IX_audit_procedure_results_firm_id_client_id_engagement_id_aud~",
                table: "audit_procedure_results",
                columns: new[] { "firm_id", "client_id", "engagement_id", "audit_procedure_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_procedure_results_firm_id_client_id_engagement_id_wor~",
                table: "audit_procedure_results",
                columns: new[] { "firm_id", "client_id", "engagement_id", "workpaper_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_procedure_results_firm_id_prepared_by_user_id",
                table: "audit_procedure_results",
                columns: new[] { "firm_id", "prepared_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_procedure_results_firm_id_reviewed_by_user_id",
                table: "audit_procedure_results",
                columns: new[] { "firm_id", "reviewed_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_audit_procedure_result_revision",
                table: "audit_procedure_results",
                columns: new[] { "firm_id", "audit_procedure_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_procedure_reviews_firm_id_client_id_engagement_id_au~1",
                table: "audit_procedure_reviews",
                columns: new[] { "firm_id", "client_id", "engagement_id", "audit_procedure_result_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_procedure_reviews_firm_id_client_id_engagement_id_aud~",
                table: "audit_procedure_reviews",
                columns: new[] { "firm_id", "client_id", "engagement_id", "audit_procedure_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_procedure_reviews_firm_id_reviewer_user_id",
                table: "audit_procedure_reviews",
                columns: new[] { "firm_id", "reviewer_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_procedure_reviews_result_created",
                table: "audit_procedure_reviews",
                columns: new[] { "firm_id", "audit_procedure_result_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_program_procedures_version_order",
                table: "audit_program_procedures",
                columns: new[] { "firm_id", "program_version_id", "section_number", "ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_program_versions_firm_id_approved_by_user_id",
                table: "audit_program_versions",
                columns: new[] { "firm_id", "approved_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_program_versions_firm_id_created_by_user_id",
                table: "audit_program_versions",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_audit_program_version_code_version",
                table: "audit_program_versions",
                columns: new[] { "firm_id", "program_code", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_engagement_audit_programs_firm_id_adopted_by_user_id",
                table: "engagement_audit_programs",
                columns: new[] { "firm_id", "adopted_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_engagement_audit_programs_firm_id_program_version_id",
                table: "engagement_audit_programs",
                columns: new[] { "firm_id", "program_version_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_audit_procedures_audit_program_procedures_firm_id_program_p~",
                table: "audit_procedures",
                columns: new[] { "firm_id", "program_procedure_id" },
                principalTable: "audit_program_procedures",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_audit_procedures_engagement_audit_programs_firm_id_client_i~",
                table: "audit_procedures",
                columns: new[] { "firm_id", "client_id", "engagement_id", "engagement_program_id" },
                principalTable: "engagement_audit_programs",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_audit_procedures_users_firm_id_applicability_decided_by_use~",
                table: "audit_procedures",
                columns: new[] { "firm_id", "applicability_decided_by_user_id" },
                principalTable: "users",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_audit_procedures_audit_program_procedures_firm_id_program_p~",
                table: "audit_procedures");

            migrationBuilder.DropForeignKey(
                name: "FK_audit_procedures_engagement_audit_programs_firm_id_client_i~",
                table: "audit_procedures");

            migrationBuilder.DropForeignKey(
                name: "FK_audit_procedures_users_firm_id_applicability_decided_by_use~",
                table: "audit_procedures");

            migrationBuilder.DropTable(
                name: "audit_procedure_reviews");

            migrationBuilder.DropTable(
                name: "audit_program_procedures");

            migrationBuilder.DropTable(
                name: "engagement_audit_programs");

            migrationBuilder.DropTable(
                name: "audit_procedure_results");

            migrationBuilder.DropTable(
                name: "audit_program_versions");

            migrationBuilder.DropIndex(
                name: "IX_audit_procedures_firm_id_applicability_decided_by_user_id",
                table: "audit_procedures");

            migrationBuilder.DropIndex(
                name: "IX_audit_procedures_firm_id_client_id_engagement_id_engagement~",
                table: "audit_procedures");

            migrationBuilder.DropIndex(
                name: "IX_audit_procedures_firm_id_program_procedure_id",
                table: "audit_procedures");

            migrationBuilder.DropIndex(
                name: "ux_audit_procedure_scope_source",
                table: "audit_procedures");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_procedure_values",
                table: "audit_procedures");

            migrationBuilder.DropColumn(
                name: "applicability_decided_at",
                table: "audit_procedures");

            migrationBuilder.DropColumn(
                name: "applicability_decided_by_user_id",
                table: "audit_procedures");

            migrationBuilder.DropColumn(
                name: "applicability_rationale",
                table: "audit_procedures");

            migrationBuilder.DropColumn(
                name: "applicability_status",
                table: "audit_procedures");

            migrationBuilder.DropColumn(
                name: "current_result_revision",
                table: "audit_procedures");

            migrationBuilder.DropColumn(
                name: "engagement_program_id",
                table: "audit_procedures");

            migrationBuilder.DropColumn(
                name: "program_procedure_id",
                table: "audit_procedures");

            migrationBuilder.DropColumn(
                name: "source_procedure_id",
                table: "audit_procedures");

            migrationBuilder.DropColumn(
                name: "source_section_number",
                table: "audit_procedures");

            migrationBuilder.DropColumn(
                name: "source_section_title",
                table: "audit_procedures");

            migrationBuilder.DropColumn(
                name: "source_wording",
                table: "audit_procedures");

            migrationBuilder.AlterColumn<Guid>(
                name: "risk_id",
                table: "audit_procedures",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_procedure_values",
                table: "audit_procedures",
                sql: "length(trim(title)) > 0");
        }
    }
}
