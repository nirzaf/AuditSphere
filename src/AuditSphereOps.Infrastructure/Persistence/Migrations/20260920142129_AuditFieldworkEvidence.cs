using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditFieldworkEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_area_assessments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_id = table.Column<Guid>(type: "uuid", nullable: true),
                    area_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    assessment_kind = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    methodology_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    input_snapshot_json = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    booked_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: true),
                    audited_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: true),
                    residual_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: true),
                    variance_percent = table.Column<decimal>(type: "numeric(19,6)", nullable: true),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    period_start = table.Column<DateOnly>(type: "date", nullable: true),
                    period_end = table.Column<DateOnly>(type: "date", nullable: true),
                    evidence_references_json = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    input_generation = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    conclusion = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_area_assessments", x => x.id);
                    table.UniqueConstraint("AK_audit_area_assessments_firm_id_id", x => new { x.firm_id, x.id });
                    table.UniqueConstraint("AK_audit_area_assessments_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.CheckConstraint("ck_audit_area_assessment_values", "area_code IN ('CASH_BANK','RECEIVABLES','INVENTORY','REVENUE','PAYABLES','FIXED_ASSETS','EXPENSES','PAYROLL','LOANS','EQUITY','RELATED_PARTIES','TAX_STATUTORY','JOURNALS_FRAUD','ANALYTICAL_REVIEW','GOING_CONCERN','SUBSEQUENT_EVENTS','FINANCIAL_STATEMENTS','AUDIT_DIFFERENCES') AND length(trim(assessment_kind)) > 0 AND length(trim(methodology_reference)) > 0 AND length(trim(input_snapshot_json)) > 0 AND length(trim(evidence_references_json)) > 0 AND length(trim(conclusion)) > 0 AND input_generation > 0 AND revision > 0 AND status IN ('SUBMITTED','REVIEWED','CHANGES_REQUIRED')");
                    table.ForeignKey(
                        name: "FK_audit_area_assessments_audit_procedures_firm_id_client_id_e~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.procedure_id },
                        principalTable: "audit_procedures",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_area_assessments_engagements_firm_id_client_id_engage~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_area_assessments_users_firm_id_created_by_user_id",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_area_assessments_users_firm_id_reviewed_by_user_id",
                        columns: x => new { x.firm_id, x.reviewed_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_confirmation_cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_id = table.Column<Guid>(type: "uuid", nullable: true),
                    area_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    source_record_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    booked_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    confirmation_date = table.Column<DateOnly>(type: "date", nullable: false),
                    respondent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    contact_validation_source = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    input_generation = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    dispatch_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_confirmation_cases", x => x.id);
                    table.UniqueConstraint("AK_audit_confirmation_cases_firm_id_id", x => new { x.firm_id, x.id });
                    table.UniqueConstraint("AK_audit_confirmation_cases_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.CheckConstraint("ck_audit_confirmation_case_values", "length(trim(area_code)) > 0 AND length(trim(source_record_id)) > 0 AND currency ~ '^[A-Z]{3}$' AND length(trim(respondent)) > 0 AND length(trim(contact_validation_source)) > 0 AND input_generation > 0 AND status IN ('DRAFT','APPROVED','DISPATCHED','RESPONSE_RECEIVED','NO_RESPONSE','ALTERNATIVE_REQUIRED','CLOSED')");
                    table.ForeignKey(
                        name: "FK_audit_confirmation_cases_audit_procedures_firm_id_client_id~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.procedure_id },
                        principalTable: "audit_procedures",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_confirmation_cases_engagements_firm_id_client_id_enga~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_confirmation_cases_users_firm_id_created_by_user_id",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_differences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_id = table.Column<Guid>(type: "uuid", nullable: true),
                    account_area = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    difference_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    corrected = table.Column<bool>(type: "boolean", nullable: false),
                    management_response = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    correction_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    evaluation = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    input_generation = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evaluated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    evaluated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_differences", x => x.id);
                    table.UniqueConstraint("AK_audit_differences_firm_id_id", x => new { x.firm_id, x.id });
                    table.UniqueConstraint("AK_audit_differences_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.CheckConstraint("ck_audit_difference_values", "length(trim(account_area)) > 0 AND length(trim(difference_type)) > 0 AND length(trim(description)) > 0 AND currency ~ '^[A-Z]{3}$' AND amount <> 0 AND input_generation > 0 AND status IN ('OPEN','MANAGEMENT_RESPONDED','EVALUATED','CORRECTED') AND ((corrected AND correction_reference IS NOT NULL AND status = 'CORRECTED') OR NOT corrected)");
                    table.ForeignKey(
                        name: "FK_audit_differences_audit_procedures_firm_id_client_id_engage~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.procedure_id },
                        principalTable: "audit_procedures",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_differences_engagements_firm_id_client_id_engagement_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_differences_users_firm_id_created_by_user_id",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_differences_users_firm_id_evaluated_by_user_id",
                        columns: x => new { x.firm_id, x.evaluated_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_schedules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    schedule_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_identifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source_receipt_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    as_of_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    period_start = table.Column<DateOnly>(type: "date", nullable: true),
                    period_end = table.Column<DateOnly>(type: "date", nullable: true),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    sign_convention = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    row_count = table.Column<int>(type: "integer", nullable: false),
                    signed_control_total = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    gl_control_total = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    residual = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    input_generation = table.Column<long>(type: "bigint", nullable: false),
                    completeness_decision = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_schedules", x => x.id);
                    table.UniqueConstraint("AK_audit_schedules_firm_id_id", x => new { x.firm_id, x.id });
                    table.UniqueConstraint("AK_audit_schedules_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.CheckConstraint("ck_audit_schedule_values", "length(trim(schedule_type)) > 0 AND length(trim(entity_identifier)) > 0 AND length(trim(source_receipt_reference)) > 0 AND length(source_hash) = 64 AND currency ~ '^[A-Z]{3}$' AND length(trim(sign_convention)) > 0 AND row_count >= 0 AND input_generation > 0 AND status IN ('PENDING_REVIEW','RECONCILED','UNRECONCILED','APPROVED','SUPERSEDED')");
                    table.ForeignKey(
                        name: "FK_audit_schedules_engagements_firm_id_client_id_engagement_id",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_schedules_users_firm_id_created_by_user_id",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_alternative_procedures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    confirmation_case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    evidence_references_json = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    conclusion = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_alternative_procedures", x => x.id);
                    table.UniqueConstraint("AK_audit_alternatives_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_audit_alternative_values", "length(trim(purpose)) > 0 AND length(trim(evidence_references_json)) > 0 AND length(trim(conclusion)) > 0 AND status IN ('SUBMITTED','REVIEWED')");
                    table.ForeignKey(
                        name: "FK_audit_alternative_procedures_audit_confirmation_cases_firm_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.confirmation_case_id },
                        principalTable: "audit_confirmation_cases",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_alternative_procedures_engagements_firm_id_client_id_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_alternative_procedures_users_firm_id_created_by_user_~",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_alternative_procedures_users_firm_id_reviewed_by_user~",
                        columns: x => new { x.firm_id, x.reviewed_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_confirmation_responses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    confirmation_case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    origin = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    channel = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    response_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    confirmed_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: true),
                    difference_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: true),
                    authenticity_assessment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    decision = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_confirmation_responses", x => x.id);
                    table.UniqueConstraint("AK_audit_confirmation_responses_case_revision", x => new { x.firm_id, x.confirmation_case_id, x.revision });
                    table.UniqueConstraint("AK_audit_confirmation_responses_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_audit_confirmation_response_values", "revision > 0 AND length(trim(origin)) > 0 AND length(trim(channel)) > 0 AND length(trim(response_reference)) > 0 AND length(trim(authenticity_assessment)) > 0 AND decision IN ('PENDING','AGREED','DIFFERENCE','NO_RESPONSE','ALTERNATIVE_REQUIRED')");
                    table.ForeignKey(
                        name: "FK_audit_confirmation_responses_audit_confirmation_cases_firm_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.confirmation_case_id },
                        principalTable: "audit_confirmation_cases",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_confirmation_responses_engagements_firm_id_client_id_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_confirmation_responses_users_firm_id_reviewed_by_user~",
                        columns: x => new { x.firm_id, x.reviewed_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_schedule_rows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stable_row_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source_line_number = table.Column<int>(type: "integer", nullable: false),
                    account_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    signed_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    transaction_date = table.Column<DateOnly>(type: "date", nullable: true),
                    posting_date = table.Column<DateOnly>(type: "date", nullable: true),
                    delivery_date = table.Column<DateOnly>(type: "date", nullable: true),
                    service_date = table.Column<DateOnly>(type: "date", nullable: true),
                    original_values_json = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_schedule_rows", x => x.id);
                    table.UniqueConstraint("AK_audit_schedule_rows_firm_id_client_id_engagement_id_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.UniqueConstraint("AK_audit_schedule_rows_firm_id_id", x => new { x.firm_id, x.id });
                    table.UniqueConstraint("AK_audit_schedule_rows_schedule_stable", x => new { x.firm_id, x.schedule_id, x.stable_row_id });
                    table.CheckConstraint("ck_audit_schedule_row_values", "source_line_number > 0 AND length(trim(stable_row_id)) > 0 AND length(trim(account_code)) > 0 AND length(trim(description)) > 0 AND currency ~ '^[A-Z]{3}$' AND length(trim(original_values_json)) > 0");
                    table.ForeignKey(
                        name: "FK_audit_schedule_rows_audit_schedules_firm_id_client_id_engag~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.schedule_id },
                        principalTable: "audit_schedules",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_schedule_rows_engagements_firm_id_client_id_engagemen~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_selections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    population_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    procedure_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    rationale = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    selected_count = table.Column<int>(type: "integer", nullable: false),
                    selected_signed_total = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    input_generation = table.Column<long>(type: "bigint", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_selections", x => x.id);
                    table.UniqueConstraint("AK_audit_selections_firm_id_id", x => new { x.firm_id, x.id });
                    table.UniqueConstraint("AK_audit_selections_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.CheckConstraint("ck_audit_selection_values", "length(trim(method)) > 0 AND length(trim(rationale)) > 0 AND selected_count > 0 AND input_generation > 0 AND status IN ('SUBMITTED','REVIEWED','CHANGES_REQUIRED')");
                    table.ForeignKey(
                        name: "FK_audit_selections_audit_procedures_firm_id_client_id_engagem~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.procedure_id },
                        principalTable: "audit_procedures",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_selections_audit_schedules_firm_id_client_id_engageme~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.schedule_id },
                        principalTable: "audit_schedules",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_selections_engagements_firm_id_client_id_engagement_id",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_selections_population_versions_firm_id_client_id_enga~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.population_version_id },
                        principalTable: "population_versions",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_selections_users_firm_id_created_by_user_id",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_selections_users_firm_id_reviewed_by_user_id",
                        columns: x => new { x.firm_id, x.reviewed_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_selection_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    selection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    schedule_row_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stable_row_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    signed_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    inclusion_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_selection_items", x => x.id);
                    table.UniqueConstraint("AK_audit_selection_items_firm_id_client_id_engagement_id_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.UniqueConstraint("AK_audit_selection_items_firm_id_id", x => new { x.firm_id, x.id });
                    table.UniqueConstraint("AK_audit_selection_items_selection_stable", x => new { x.firm_id, x.selection_id, x.stable_row_id });
                    table.CheckConstraint("ck_audit_selection_item_values", "length(trim(stable_row_id)) > 0 AND currency ~ '^[A-Z]{3}$' AND length(trim(inclusion_reason)) > 0");
                    table.ForeignKey(
                        name: "FK_audit_selection_items_audit_schedule_rows_firm_id_client_id~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.schedule_row_id },
                        principalTable: "audit_schedule_rows",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_selection_items_audit_selections_firm_id_client_id_en~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.selection_id },
                        principalTable: "audit_selections",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_selection_items_engagements_firm_id_client_id_engagem~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_item_tests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    selection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    selection_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    work_performed = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    evidence_references_json = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    result = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    exception_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: true),
                    contradictory_evidence = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    follow_up = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    tested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_item_tests", x => x.id);
                    table.UniqueConstraint("AK_audit_item_tests_firm_id_client_id_engagement_id_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.UniqueConstraint("AK_audit_item_tests_firm_id_id", x => new { x.firm_id, x.id });
                    table.UniqueConstraint("AK_audit_item_tests_item_revision", x => new { x.firm_id, x.selection_item_id, x.revision });
                    table.CheckConstraint("ck_audit_item_test_values", "revision > 0 AND length(trim(work_performed)) > 0 AND length(trim(evidence_references_json)) > 0 AND result IN ('PENDING','PASS','EXCEPTION','LIMITATION')");
                    table.ForeignKey(
                        name: "FK_audit_item_tests_audit_procedures_firm_id_client_id_engagem~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.procedure_id },
                        principalTable: "audit_procedures",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_item_tests_audit_selection_items_firm_id_client_id_en~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.selection_item_id },
                        principalTable: "audit_selection_items",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_item_tests_audit_selections_firm_id_client_id_engagem~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.selection_id },
                        principalTable: "audit_selections",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_item_tests_engagements_firm_id_client_id_engagement_id",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_item_tests_users_firm_id_tested_by_user_id",
                        columns: x => new { x.firm_id, x.tested_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_item_test_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    selection_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audit_item_test_id = table.Column<Guid>(type: "uuid", nullable: false),
                    test_revision = table.Column<long>(type: "bigint", nullable: false),
                    decision = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    comment = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    reviewer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_item_test_reviews", x => x.id);
                    table.UniqueConstraint("AK_audit_item_test_reviews_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_audit_item_test_review_values", "test_revision > 0 AND decision IN ('REVIEWED','CHANGES_REQUIRED')");
                    table.ForeignKey(
                        name: "FK_audit_item_test_reviews_audit_item_tests_firm_id_client_id_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.audit_item_test_id },
                        principalTable: "audit_item_tests",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_item_test_reviews_audit_selection_items_firm_id_clien~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.selection_item_id },
                        principalTable: "audit_selection_items",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_item_test_reviews_engagements_firm_id_client_id_engag~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_item_test_reviews_users_firm_id_reviewer_user_id",
                        columns: x => new { x.firm_id, x.reviewer_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_alternative_procedures_firm_id_client_id_engagement_i~",
                table: "audit_alternative_procedures",
                columns: new[] { "firm_id", "client_id", "engagement_id", "confirmation_case_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_alternative_procedures_firm_id_created_by_user_id",
                table: "audit_alternative_procedures",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_alternative_procedures_firm_id_reviewed_by_user_id",
                table: "audit_alternative_procedures",
                columns: new[] { "firm_id", "reviewed_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_area_assessments_firm_id_client_id_engagement_id_proc~",
                table: "audit_area_assessments",
                columns: new[] { "firm_id", "client_id", "engagement_id", "procedure_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_area_assessments_firm_id_created_by_user_id",
                table: "audit_area_assessments",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_area_assessments_firm_id_reviewed_by_user_id",
                table: "audit_area_assessments",
                columns: new[] { "firm_id", "reviewed_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_confirmation_cases_firm_id_client_id_engagement_id_pr~",
                table: "audit_confirmation_cases",
                columns: new[] { "firm_id", "client_id", "engagement_id", "procedure_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_confirmation_cases_firm_id_created_by_user_id",
                table: "audit_confirmation_cases",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_confirmation_responses_firm_id_client_id_engagement_i~",
                table: "audit_confirmation_responses",
                columns: new[] { "firm_id", "client_id", "engagement_id", "confirmation_case_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_confirmation_responses_firm_id_reviewed_by_user_id",
                table: "audit_confirmation_responses",
                columns: new[] { "firm_id", "reviewed_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_differences_firm_id_client_id_engagement_id_procedure~",
                table: "audit_differences",
                columns: new[] { "firm_id", "client_id", "engagement_id", "procedure_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_differences_firm_id_created_by_user_id",
                table: "audit_differences",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_differences_firm_id_evaluated_by_user_id",
                table: "audit_differences",
                columns: new[] { "firm_id", "evaluated_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_item_test_reviews_firm_id_client_id_engagement_id_aud~",
                table: "audit_item_test_reviews",
                columns: new[] { "firm_id", "client_id", "engagement_id", "audit_item_test_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_item_test_reviews_firm_id_client_id_engagement_id_sel~",
                table: "audit_item_test_reviews",
                columns: new[] { "firm_id", "client_id", "engagement_id", "selection_item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_item_test_reviews_firm_id_reviewer_user_id",
                table: "audit_item_test_reviews",
                columns: new[] { "firm_id", "reviewer_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_item_tests_firm_id_client_id_engagement_id_procedure_~",
                table: "audit_item_tests",
                columns: new[] { "firm_id", "client_id", "engagement_id", "procedure_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_item_tests_firm_id_client_id_engagement_id_selection_~",
                table: "audit_item_tests",
                columns: new[] { "firm_id", "client_id", "engagement_id", "selection_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_item_tests_firm_id_client_id_engagement_id_selection~1",
                table: "audit_item_tests",
                columns: new[] { "firm_id", "client_id", "engagement_id", "selection_item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_item_tests_firm_id_tested_by_user_id",
                table: "audit_item_tests",
                columns: new[] { "firm_id", "tested_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_schedule_rows_firm_id_client_id_engagement_id_schedul~",
                table: "audit_schedule_rows",
                columns: new[] { "firm_id", "client_id", "engagement_id", "schedule_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_schedule_rows_order",
                table: "audit_schedule_rows",
                columns: new[] { "firm_id", "schedule_id", "source_line_number" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_schedule_scope_type_date",
                table: "audit_schedules",
                columns: new[] { "firm_id", "engagement_id", "schedule_type", "as_of_date" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_schedules_firm_id_created_by_user_id",
                table: "audit_schedules",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_audit_schedule_source_version",
                table: "audit_schedules",
                columns: new[] { "firm_id", "engagement_id", "source_receipt_reference", "source_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_selection_items_firm_id_client_id_engagement_id_sched~",
                table: "audit_selection_items",
                columns: new[] { "firm_id", "client_id", "engagement_id", "schedule_row_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_selection_items_firm_id_client_id_engagement_id_selec~",
                table: "audit_selection_items",
                columns: new[] { "firm_id", "client_id", "engagement_id", "selection_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_selections_firm_id_client_id_engagement_id_population~",
                table: "audit_selections",
                columns: new[] { "firm_id", "client_id", "engagement_id", "population_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_selections_firm_id_client_id_engagement_id_procedure_~",
                table: "audit_selections",
                columns: new[] { "firm_id", "client_id", "engagement_id", "procedure_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_selections_firm_id_client_id_engagement_id_schedule_id",
                table: "audit_selections",
                columns: new[] { "firm_id", "client_id", "engagement_id", "schedule_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_selections_firm_id_created_by_user_id",
                table: "audit_selections",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_selections_firm_id_reviewed_by_user_id",
                table: "audit_selections",
                columns: new[] { "firm_id", "reviewed_by_user_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_alternative_procedures");

            migrationBuilder.DropTable(
                name: "audit_area_assessments");

            migrationBuilder.DropTable(
                name: "audit_confirmation_responses");

            migrationBuilder.DropTable(
                name: "audit_differences");

            migrationBuilder.DropTable(
                name: "audit_item_test_reviews");

            migrationBuilder.DropTable(
                name: "audit_confirmation_cases");

            migrationBuilder.DropTable(
                name: "audit_item_tests");

            migrationBuilder.DropTable(
                name: "audit_selection_items");

            migrationBuilder.DropTable(
                name: "audit_schedule_rows");

            migrationBuilder.DropTable(
                name: "audit_selections");

            migrationBuilder.DropTable(
                name: "audit_schedules");
        }
    }
}
