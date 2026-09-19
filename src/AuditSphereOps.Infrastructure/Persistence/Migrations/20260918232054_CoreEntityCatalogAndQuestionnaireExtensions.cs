using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CoreEntityCatalogAndQuestionnaireExtensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "engagement_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    allocated_hours = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_engagement_assignments", x => x.id);
                    table.ForeignKey(
                        name: "FK_engagement_assignments_engagements_engagement_id",
                        column: x => x.engagement_id,
                        principalTable: "engagements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_engagement_assignments_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "eqr_cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    eqr_partner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    concurrence_date = table.Column<DateOnly>(type: "date", nullable: true),
                    findings_discussed = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_eqr_cases", x => x.id);
                    table.ForeignKey(
                        name: "FK_eqr_cases_engagements_engagement_id",
                        column: x => x.engagement_id,
                        principalTable: "engagements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_eqr_cases_users_eqr_partner_user_id",
                        column: x => x.eqr_partner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "questionnaire_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_questionnaire_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "source_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_type = table.Column<string>(type: "text", nullable: false),
                    receipt_token = table.Column<string>(type: "text", nullable: false),
                    sha256_digest = table.Column<string>(type: "text", nullable: false),
                    byte_count = table.Column<long>(type: "bigint", nullable: false),
                    original_file_name = table.Column<string>(type: "text", nullable: false),
                    acquired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    acquired_by_user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_receipts", x => x.id);
                    table.ForeignKey(
                        name: "FK_source_receipts_engagements_engagement_id",
                        column: x => x.engagement_id,
                        principalTable: "engagements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "specialist_clearances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    practice_client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    area = table.Column<string>(type: "text", nullable: false),
                    specialist_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    specialist_name = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    evidence_reference = table.Column<string>(type: "text", nullable: true),
                    conditions = table.Column<string>(type: "text", nullable: true),
                    cleared_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_specialist_clearances", x => x.id);
                    table.ForeignKey(
                        name: "FK_specialist_clearances_practice_clients_practice_client_id",
                        column: x => x.practice_client_id,
                        principalTable: "practice_clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "written_representations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    narrative = table.Column<string>(type: "text", nullable: false),
                    obtained = table.Column<bool>(type: "boolean", nullable: false),
                    obtained_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    signatory_name = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_written_representations", x => x.id);
                    table.ForeignKey(
                        name: "FK_written_representations_engagements_engagement_id",
                        column: x => x.engagement_id,
                        principalTable: "engagements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "question_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_code = table.Column<string>(type: "text", nullable: false),
                    section = table.Column<string>(type: "text", nullable: false),
                    prompt_text = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    answer_type = table.Column<string>(type: "text", nullable: false),
                    requires_evidence = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_question_definitions", x => x.id);
                    table.ForeignKey(
                        name: "FK_question_definitions_questionnaire_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "questionnaire_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "evidence_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workpaper_id = table.Column<Guid>(type: "uuid", nullable: true),
                    purpose = table.Column<string>(type: "text", nullable: false),
                    assertion = table.Column<string>(type: "text", nullable: false),
                    relevance_reliability_assessment = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evidence_links", x => x.id);
                    table.ForeignKey(
                        name: "FK_evidence_links_source_receipts_source_receipt_id",
                        column: x => x.source_receipt_id,
                        principalTable: "source_receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_engagement_assignments_engagement_id_user_id_role",
                table: "engagement_assignments",
                columns: new[] { "engagement_id", "user_id", "role" });

            migrationBuilder.CreateIndex(
                name: "IX_engagement_assignments_user_id",
                table: "engagement_assignments",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_eqr_cases_engagement_id",
                table: "eqr_cases",
                column: "engagement_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_eqr_cases_eqr_partner_user_id",
                table: "eqr_cases",
                column: "eqr_partner_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_evidence_links_engagement_id_source_receipt_id",
                table: "evidence_links",
                columns: new[] { "engagement_id", "source_receipt_id" });

            migrationBuilder.CreateIndex(
                name: "IX_evidence_links_source_receipt_id",
                table: "evidence_links",
                column: "source_receipt_id");

            migrationBuilder.CreateIndex(
                name: "IX_question_definitions_template_id_question_code",
                table: "question_definitions",
                columns: new[] { "template_id", "question_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_questionnaire_templates_bank_version",
                table: "questionnaire_templates",
                columns: new[] { "bank", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_source_receipts_engagement_id_receipt_token",
                table: "source_receipts",
                columns: new[] { "engagement_id", "receipt_token" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_specialist_clearances_practice_client_id_area",
                table: "specialist_clearances",
                columns: new[] { "practice_client_id", "area" });

            migrationBuilder.CreateIndex(
                name: "IX_written_representations_engagement_id_code",
                table: "written_representations",
                columns: new[] { "engagement_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "engagement_assignments");

            migrationBuilder.DropTable(
                name: "eqr_cases");

            migrationBuilder.DropTable(
                name: "evidence_links");

            migrationBuilder.DropTable(
                name: "question_definitions");

            migrationBuilder.DropTable(
                name: "specialist_clearances");

            migrationBuilder.DropTable(
                name: "written_representations");

            migrationBuilder.DropTable(
                name: "source_receipts");

            migrationBuilder.DropTable(
                name: "questionnaire_templates");
        }
    }
}
