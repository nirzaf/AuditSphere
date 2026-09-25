using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditSamplingCutOffAndSubsequentMatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_cut_off_test_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    selection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    selection_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    transaction_date = table.Column<DateOnly>(type: "date", nullable: false),
                    document_date = table.Column<DateOnly>(type: "date", nullable: true),
                    ship_receive_date = table.Column<DateOnly>(type: "date", nullable: true),
                    period_end_indicator = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    is_cut_off_exception = table.Column<bool>(type: "boolean", nullable: false),
                    work_performed = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    evidence_references_json = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_cut_off_test_records", x => x.id);
                    table.UniqueConstraint("AK_audit_cut_off_tests_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_audit_cut_off_test_values", "revision > 0 AND period_end_indicator IN ('BEFORE_PERIOD_END','AFTER_PERIOD_END')");
                    table.ForeignKey(
                        name: "FK_audit_cut_off_test_records_audit_selection_items_firm_id_cl~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.selection_item_id },
                        principalTable: "audit_selection_items",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_cut_off_test_records_audit_selections_firm_id_client_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.selection_id },
                        principalTable: "audit_selections",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_cut_off_test_records_engagements_firm_id_client_id_en~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_subsequent_match_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    selection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    selection_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_signed_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    matched_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    subsequent_source_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    subsequent_date = table.Column<DateOnly>(type: "date", nullable: true),
                    state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    unmatched_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    evidence_reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_subsequent_match_records", x => x.id);
                    table.UniqueConstraint("AK_audit_subsequent_matches_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_audit_subsequent_match_values", "revision > 0 AND currency ~ '^[A-Z]{3}$' AND matched_amount >= 0 AND state IN ('MATCHED','PARTIALLY_MATCHED','UNMATCHED')");
                    table.ForeignKey(
                        name: "FK_audit_subsequent_match_records_audit_selection_items_firm_i~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.selection_item_id },
                        principalTable: "audit_selection_items",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_subsequent_match_records_audit_selections_firm_id_cli~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.selection_id },
                        principalTable: "audit_selections",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_subsequent_match_records_engagements_firm_id_client_i~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_cut_off_test_records_firm_id_client_id_engagement_id_~",
                table: "audit_cut_off_test_records",
                columns: new[] { "firm_id", "client_id", "engagement_id", "selection_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_cut_off_test_records_firm_id_client_id_engagement_id~1",
                table: "audit_cut_off_test_records",
                columns: new[] { "firm_id", "client_id", "engagement_id", "selection_item_id" });

            migrationBuilder.CreateIndex(
                name: "ux_audit_cut_off_test_item",
                table: "audit_cut_off_test_records",
                columns: new[] { "firm_id", "engagement_id", "selection_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_subsequent_match_records_firm_id_client_id_engagemen~1",
                table: "audit_subsequent_match_records",
                columns: new[] { "firm_id", "client_id", "engagement_id", "selection_item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_subsequent_match_records_firm_id_client_id_engagement~",
                table: "audit_subsequent_match_records",
                columns: new[] { "firm_id", "client_id", "engagement_id", "selection_id" });

            migrationBuilder.CreateIndex(
                name: "ux_audit_subsequent_match_item",
                table: "audit_subsequent_match_records",
                columns: new[] { "firm_id", "engagement_id", "selection_item_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_cut_off_test_records");

            migrationBuilder.DropTable(
                name: "audit_subsequent_match_records");
        }
    }
}
