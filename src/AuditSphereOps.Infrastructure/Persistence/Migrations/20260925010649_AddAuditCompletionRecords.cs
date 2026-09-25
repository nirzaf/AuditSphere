using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditCompletionRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "analytical_variance_investigations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_id = table.Column<Guid>(type: "uuid", nullable: true),
                    account_area = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    period_reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    expected_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    actual_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    difference_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    difference_percent = table.Column<decimal>(type: "numeric(19,6)", nullable: true),
                    investigation_threshold = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    exceeds_threshold = table.Column<bool>(type: "boolean", nullable: false),
                    explanation = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    conclusion = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    evidence_references_json = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analytical_variance_investigations", x => x.id);
                    table.UniqueConstraint("AK_analytical_variance_investigations_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_analytical_variance_values", "revision > 0 AND currency ~ '^[A-Z]{3}$' AND investigation_threshold >= 0 AND conclusion IN ('EXPLAINED','UNEXPLAINED','CORROBORATED')");
                    table.ForeignKey(
                        name: "FK_analytical_variance_investigations_engagements_firm_id_clie~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "going_concern_assessments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assessment_date = table.Column<DateOnly>(type: "date", nullable: false),
                    period_covered_to = table.Column<DateOnly>(type: "date", nullable: false),
                    forecast_review_outcome = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    material_uncertainty_identified = table.Column<bool>(type: "boolean", nullable: false),
                    disclosure_adequate = table.Column<bool>(type: "boolean", nullable: false),
                    conclusion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    rationale = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    evidence_references_json = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_going_concern_assessments", x => x.id);
                    table.UniqueConstraint("AK_going_concern_assessments_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_going_concern_values", "revision > 0 AND currency ~ '^[A-Z]{3}$' AND period_covered_to >= assessment_date AND conclusion IN ('NO_MATERIAL_UNCERTAINTY','MATERIAL_UNCERTAINTY_DISCLOSED','INADEQUATE_DISCLOSURE','NOT_ASSESSED')");
                    table.ForeignKey(
                        name: "FK_going_concern_assessments_engagements_firm_id_client_id_eng~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "subsequent_event_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_id = table.Column<Guid>(type: "uuid", nullable: true),
                    period_end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    event_date = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    classification = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    adjustment_required = table.Column<bool>(type: "boolean", nullable: false),
                    disclosure_required = table.Column<bool>(type: "boolean", nullable: false),
                    disclosure_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    rationale = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    financial_effect = table.Column<decimal>(type: "numeric(19,6)", nullable: true),
                    evidence_references_json = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subsequent_event_reviews", x => x.id);
                    table.UniqueConstraint("AK_subsequent_event_reviews_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_subsequent_event_values", "revision > 0 AND currency ~ '^[A-Z]{3}$' AND event_date >= period_end_date AND classification IN ('ADJUSTING','NON_ADJUSTING','PENDING_ASSESSMENT')");
                    table.ForeignKey(
                        name: "FK_subsequent_event_reviews_engagements_firm_id_client_id_enga~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_analytical_variance_investigations_firm_id_client_id_engage~",
                table: "analytical_variance_investigations",
                columns: new[] { "firm_id", "client_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "ux_analytical_variance_engagement_area_period",
                table: "analytical_variance_investigations",
                columns: new[] { "firm_id", "engagement_id", "account_area", "period_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_going_concern_assessments_firm_id_client_id_engagement_id",
                table: "going_concern_assessments",
                columns: new[] { "firm_id", "client_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "ux_going_concern_engagement_date",
                table: "going_concern_assessments",
                columns: new[] { "firm_id", "engagement_id", "assessment_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subsequent_event_reviews_firm_id_client_id_engagement_id",
                table: "subsequent_event_reviews",
                columns: new[] { "firm_id", "client_id", "engagement_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "analytical_variance_investigations");

            migrationBuilder.DropTable(
                name: "going_concern_assessments");

            migrationBuilder.DropTable(
                name: "subsequent_event_reviews");
        }
    }
}
