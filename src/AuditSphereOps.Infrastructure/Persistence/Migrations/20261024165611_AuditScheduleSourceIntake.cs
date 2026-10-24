using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditScheduleSourceIntake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_source_acceptance_decisions_scope_id",
                table: "source_acceptance_decisions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_pbc_acceptance_receipts_scope_id",
                table: "pbc_acceptance_receipts",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id" });

            migrationBuilder.CreateTable(
                name: "audit_schedule_source_captures",
                columns: table => new
                {
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    pbc_acceptance_receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accepted_source_decision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trial_balance_dataset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_revision = table.Column<long>(type: "bigint", nullable: false),
                    book_id = table.Column<Guid>(type: "uuid", nullable: true),
                    basis = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    legal_entity_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    profile_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_date = table.Column<DateOnly>(type: "date", nullable: false),
                    input_generation = table.Column<long>(type: "bigint", nullable: false),
                    request_json = table.Column<string>(type: "text", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    receipt_json = table.Column<string>(type: "text", nullable: false),
                    receipt_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    manifest_json = table.Column<string>(type: "text", nullable: false),
                    manifest_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_schedule_source_captures", x => x.schedule_id);
                    table.UniqueConstraint("AK_audit_schedule_source_captures_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.schedule_id });
                    table.CheckConstraint("ck_schedule_source_capture_values", "actor_epoch >= 1 AND period_revision >= 1 AND input_generation >= 1\nAND request_id <> '00000000-0000-0000-0000-000000000000'::uuid\nAND length(trim(legal_entity_key)) BETWEEN 1 AND 200\nAND (basis IS NULL OR length(trim(basis)) BETWEEN 1 AND 50)\nAND currency ~ '^[A-Z]{3}$'\nAND profile_version = 'audit-client-listing-csv.v1'\nAND request_hash ~ '^[0-9a-f]{64}$' AND receipt_hash ~ '^[0-9a-f]{64}$'\nAND manifest_hash ~ '^[0-9a-f]{64}$'\nAND octet_length(request_json) BETWEEN 2 AND 65536\nAND octet_length(receipt_json) BETWEEN 2 AND 65536\nAND octet_length(manifest_json) BETWEEN 2 AND 16777216\nAND request_hash = encode(sha256(convert_to(request_json, 'UTF8')), 'hex')\nAND receipt_hash = encode(sha256(convert_to(receipt_json, 'UTF8')), 'hex')\nAND manifest_hash = encode(sha256(convert_to(manifest_json, 'UTF8')), 'hex')\nAND jsonb_typeof(request_json::jsonb) = 'object'\nAND jsonb_typeof(receipt_json::jsonb) = 'object'\nAND jsonb_typeof(manifest_json::jsonb) = 'object'\nAND COALESCE(manifest_json::jsonb->>'Version' = 'audit-schedule-source-capture.v1', false)");
                    table.ForeignKey(
                        name: "FK_audit_schedule_source_captures_audit_schedules_firm_id_clie~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.schedule_id },
                        principalTable: "audit_schedules",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_schedule_source_captures_client_reporting_books_firm_~",
                        columns: x => new { x.firm_id, x.client_id, x.book_id },
                        principalTable: "client_reporting_books",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_schedule_source_captures_client_reporting_periods_fir~",
                        columns: x => new { x.firm_id, x.client_id, x.period_id },
                        principalTable: "client_reporting_periods",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_schedule_source_captures_pbc_acceptance_receipts_firm~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.pbc_acceptance_receipt_id },
                        principalTable: "pbc_acceptance_receipts",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_schedule_source_captures_source_acceptance_decisions_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.accepted_source_decision_id },
                        principalTable: "source_acceptance_decisions",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_schedule_source_captures_trial_balance_datasets_firm_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.trial_balance_dataset_id },
                        principalTable: "trial_balance_datasets",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_schedule_source_captures_users_firm_id_actor_user_id",
                        columns: x => new { x.firm_id, x.actor_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_schedule_source_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    input_generation = table.Column<long>(type: "bigint", nullable: false),
                    action = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    capture_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    request_json = table.Column<string>(type: "text", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    receipt_json = table.Column<string>(type: "text", nullable: false),
                    receipt_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_schedule_source_reviews", x => x.id);
                    table.CheckConstraint("ck_schedule_source_review_values", "actor_epoch >= 1 AND input_generation >= 1\nAND request_id <> '00000000-0000-0000-0000-000000000000'::uuid\nAND action IN ('APPROVE','RETURN') AND length(trim(reason)) BETWEEN 1 AND 2000\nAND length(evidence_reference) <= 2000\nAND capture_hash ~ '^[0-9a-f]{64}$'\nAND request_hash ~ '^[0-9a-f]{64}$' AND receipt_hash ~ '^[0-9a-f]{64}$'\nAND octet_length(request_json) BETWEEN 2 AND 65536\nAND octet_length(receipt_json) BETWEEN 2 AND 65536\nAND request_hash = encode(sha256(convert_to(request_json, 'UTF8')), 'hex')\nAND receipt_hash = encode(sha256(convert_to(receipt_json, 'UTF8')), 'hex')\nAND jsonb_typeof(request_json::jsonb) = 'object'\nAND jsonb_typeof(receipt_json::jsonb) = 'object'");
                    table.ForeignKey(
                        name: "FK_audit_schedule_source_reviews_audit_schedule_source_capture~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.schedule_id },
                        principalTable: "audit_schedule_source_captures",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "schedule_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_schedule_source_reviews_users_firm_id_actor_user_id",
                        columns: x => new { x.firm_id, x.actor_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_schedule_source_captures_firm_id_client_id_book_id",
                table: "audit_schedule_source_captures",
                columns: new[] { "firm_id", "client_id", "book_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_schedule_source_captures_firm_id_client_id_engagemen~1",
                table: "audit_schedule_source_captures",
                columns: new[] { "firm_id", "client_id", "engagement_id", "pbc_acceptance_receipt_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_schedule_source_captures_firm_id_client_id_engagemen~2",
                table: "audit_schedule_source_captures",
                columns: new[] { "firm_id", "client_id", "engagement_id", "trial_balance_dataset_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_schedule_source_captures_firm_id_client_id_engagement~",
                table: "audit_schedule_source_captures",
                columns: new[] { "firm_id", "client_id", "engagement_id", "accepted_source_decision_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_schedule_source_captures_firm_id_client_id_period_id",
                table: "audit_schedule_source_captures",
                columns: new[] { "firm_id", "client_id", "period_id" });

            migrationBuilder.CreateIndex(
                name: "ux_schedule_source_capture_actor_request",
                table: "audit_schedule_source_captures",
                columns: new[] { "firm_id", "actor_user_id", "actor_epoch", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_schedule_source_review_actor_request",
                table: "audit_schedule_source_reviews",
                columns: new[] { "firm_id", "actor_user_id", "actor_epoch", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_schedule_source_review_capture",
                table: "audit_schedule_source_reviews",
                columns: new[] { "firm_id", "client_id", "engagement_id", "schedule_id" },
                unique: true);
            migrationBuilder.Sql(AuditScheduleSourceIntakeSql.Install);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM audit_schedule_source_captures)
                    OR EXISTS (SELECT 1 FROM audit_schedule_source_reviews) THEN
                    RAISE EXCEPTION 'Retained audit schedule source evidence prevents downgrade.' USING ERRCODE = '55000';
                  END IF;
                END $$;
                """);
            migrationBuilder.Sql(AuditScheduleSourceIntakeSql.Uninstall);
            migrationBuilder.DropTable(
                name: "audit_schedule_source_reviews");

            migrationBuilder.DropTable(
                name: "audit_schedule_source_captures");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_source_acceptance_decisions_scope_id",
                table: "source_acceptance_decisions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_pbc_acceptance_receipts_scope_id",
                table: "pbc_acceptance_receipts");
        }
    }
}
