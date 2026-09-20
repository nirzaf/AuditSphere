using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientAccountingAndConsolidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounting_reconciliations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    book_id = table.Column<Guid>(type: "uuid", nullable: true),
                    area = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    trial_balance_dataset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    import_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    account_selection = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    as_of_date = table.Column<DateOnly>(type: "date", nullable: false),
                    source_total = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    gl_total = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    residual = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    source_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    input_generation = table.Column<long>(type: "bigint", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_reconciliations", x => x.id);
                    table.UniqueConstraint("ak_accounting_reconciliations_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.CheckConstraint("ck_accounting_reconciliation_values", "length(trim(area)) > 0 AND length(trim(account_selection)) > 0");
                    table.ForeignKey(
                        name: "FK_accounting_reconciliations_engagements_firm_id_client_id_en~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "analytical_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comparison_period_id = table.Column<Guid>(type: "uuid", nullable: true),
                    area = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    measure = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    current_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    prior_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    budget_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: true),
                    ratio = table.Column<decimal>(type: "numeric(19,6)", nullable: true),
                    denominator_basis = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    formula_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    input_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    explanation = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analytical_reviews", x => x.id);
                    table.CheckConstraint("ck_analytical_review_values", "length(trim(area)) > 0 AND length(trim(measure)) > 0 AND length(trim(denominator_basis)) > 0 AND length(trim(formula_version)) > 0");
                    table.ForeignKey(
                        name: "FK_analytical_reviews_engagements_firm_id_client_id_engagement~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "client_accounting_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    jurisdiction = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    functional_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    fiscal_year_start_month = table.Column<int>(type: "integer", nullable: false),
                    fiscal_year_start_day = table.Column<int>(type: "integer", nullable: false),
                    source_system = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_system_identifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_accounting_profiles", x => x.id);
                    table.CheckConstraint("ck_client_accounting_profile_values", "length(trim(jurisdiction)) > 0 AND functional_currency ~ '^[A-Z]{3}$' AND fiscal_year_start_month BETWEEN 1 AND 12 AND fiscal_year_start_day BETWEEN 1 AND 31");
                    table.ForeignKey(
                        name: "FK_client_accounting_profiles_practice_clients_firm_id_client_~",
                        columns: x => new { x.firm_id, x.client_id },
                        principalTable: "practice_clients",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "client_chart_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    source_scope = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    published_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_chart_versions", x => x.id);
                    table.UniqueConstraint("ak_client_chart_versions_scope_id", x => new { x.firm_id, x.client_id, x.id });
                    table.CheckConstraint("ck_client_chart_version_values", "effective_to IS NULL OR effective_from <= effective_to");
                    table.ForeignKey(
                        name: "FK_client_chart_versions_practice_clients_firm_id_client_id",
                        columns: x => new { x.firm_id, x.client_id },
                        principalTable: "practice_clients",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "client_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_groups", x => x.id);
                    table.UniqueConstraint("AK_client_groups_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_client_group_values", "length(trim(code)) > 0 AND length(trim(name)) > 0");
                });

            migrationBuilder.CreateTable(
                name: "client_reporting_periods",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    basis = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    prior_period_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    closed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    close_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_reporting_periods", x => x.id);
                    table.UniqueConstraint("ak_client_reporting_periods_scope_id", x => new { x.firm_id, x.client_id, x.id });
                    table.CheckConstraint("ck_client_reporting_period_values", "start_date <= end_date AND length(trim(period_code)) > 0 AND length(trim(basis)) > 0 AND currency ~ '^[A-Z]{3}$'");
                    table.ForeignKey(
                        name: "FK_client_reporting_periods_client_reporting_periods_firm_id_c~",
                        columns: x => new { x.firm_id, x.client_id, x.prior_period_id },
                        principalTable: "client_reporting_periods",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_reporting_periods_practice_clients_firm_id_client_id",
                        columns: x => new { x.firm_id, x.client_id },
                        principalTable: "practice_clients",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exchange_rate_set_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exchange_rate_set_versions", x => x.id);
                    table.UniqueConstraint("AK_exchange_rate_set_versions_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_exchange_rate_set_values", "length(trim(code)) > 0 AND length(trim(source)) > 0");
                });

            migrationBuilder.CreateTable(
                name: "journal_risk_flags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    import_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    score = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    disposition = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journal_risk_flags", x => x.id);
                    table.CheckConstraint("ck_journal_risk_flag_values", "length(trim(rule_code)) > 0 AND length(trim(reason)) > 0 AND score >= 0 AND score <= 100");
                    table.ForeignKey(
                        name: "FK_journal_risk_flags_engagements_firm_id_client_id_engagement~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reporting_taxonomy_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    framework = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reporting_taxonomy_versions", x => x.id);
                    table.UniqueConstraint("ak_reporting_taxonomy_versions_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_reporting_taxonomy_values", "length(trim(code)) > 0 AND length(trim(framework)) > 0 AND length(trim(name)) > 0 AND (effective_to IS NULL OR effective_from <= effective_to)");
                });

            migrationBuilder.CreateTable(
                name: "source_import_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    book_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    profile_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    parser_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    raw_file_sha256_hex = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    normalized_dataset_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    legal_entity_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    row_count = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    receipt_reference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_import_batches", x => x.id);
                    table.UniqueConstraint("AK_source_import_batches_firm_id_client_id_engagement_id_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.CheckConstraint("ck_source_import_batch_values", "length(trim(source_kind)) > 0 AND length(trim(profile_version)) > 0 AND length(trim(parser_version)) > 0 AND currency ~ '^[A-Z]{3}$' AND status IN ('LOADING','SEALED','REJECTED')");
                    table.ForeignKey(
                        name: "FK_source_import_batches_engagements_firm_id_client_id_engagem~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "specialist_accounting_schedules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    area = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    methodology_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    opening_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    additions_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    disposals_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    depreciation_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    impairment_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    interest_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    current_portion = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    non_current_portion = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    capital_movement = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    dividends = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    tax_paid = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    management_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    calculated_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    difference = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    assumptions_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_specialist_accounting_schedules", x => x.id);
                    table.CheckConstraint("ck_specialist_schedule_values", "length(trim(area)) > 0 AND length(trim(methodology_version)) > 0");
                    table.ForeignKey(
                        name: "FK_specialist_accounting_schedules_engagements_firm_id_client_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "translation_policy_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    functional_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    presentation_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    closing_rate_rule = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    average_rate_rule = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    historical_rate_rule = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_translation_policy_versions", x => x.id);
                    table.UniqueConstraint("AK_translation_policy_versions_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_translation_policy_values", "functional_currency ~ '^[A-Z]{3}$' AND presentation_currency ~ '^[A-Z]{3}$'");
                });

            migrationBuilder.CreateTable(
                name: "accounting_reconciliation_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reconciliation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stable_item_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    signed_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    item_date = table.Column<DateOnly>(type: "date", nullable: true),
                    age_days = table.Column<int>(type: "integer", nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    disposition = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_reconciliation_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_accounting_reconciliation_items_accounting_reconciliations_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.reconciliation_id },
                        principalTable: "accounting_reconciliations",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accounting_reconciliation_items_engagements_firm_id_client_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ecl_assessments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reconciliation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    as_of_date = table.Column<DateOnly>(type: "date", nullable: false),
                    method = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    methodology_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    eligible_exposure = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    probability_of_default = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    loss_given_default = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    management_overlay = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    calculated_expected_loss = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    management_expected_loss = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    difference = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    assumptions_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ecl_assessments", x => x.id);
                    table.CheckConstraint("ck_ecl_assessment_values", "length(trim(method)) > 0 AND length(trim(methodology_version)) > 0 AND eligible_exposure >= 0 AND probability_of_default BETWEEN 0 AND 1 AND loss_given_default BETWEEN 0 AND 1");
                    table.ForeignKey(
                        name: "FK_ecl_assessments_accounting_reconciliations_firm_id_client_i~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.reconciliation_id },
                        principalTable: "accounting_reconciliations",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ecl_assessments_engagements_firm_id_client_id_engagement_id",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_valuation_assessments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reconciliation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    as_of_date = table.Column<DateOnly>(type: "date", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    nrv_per_unit = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    obsolescence_reserve = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    book_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    calculated_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    difference = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    methodology_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    assumptions_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_valuation_assessments", x => x.id);
                    table.CheckConstraint("ck_inventory_valuation_values", "quantity >= 0 AND unit_cost >= 0 AND nrv_per_unit >= 0 AND obsolescence_reserve >= 0 AND book_amount >= 0");
                    table.ForeignKey(
                        name: "FK_inventory_valuation_assessments_accounting_reconciliations_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.reconciliation_id },
                        principalTable: "accounting_reconciliations",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_valuation_assessments_engagements_firm_id_client_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "client_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chart_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stable_identity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    account_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    account_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    account_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    normal_balance = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    parent_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_posting = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_accounts", x => x.id);
                    table.UniqueConstraint("AK_client_accounts_firm_id_client_id_id", x => new { x.firm_id, x.client_id, x.id });
                    table.CheckConstraint("ck_client_account_values", "length(trim(stable_identity)) > 0 AND length(trim(account_code)) > 0 AND length(trim(account_name)) > 0 AND length(trim(account_type)) > 0");
                    table.ForeignKey(
                        name: "FK_client_accounts_client_accounts_firm_id_client_id_parent_ac~",
                        columns: x => new { x.firm_id, x.client_id, x.parent_account_id },
                        principalTable: "client_accounts",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_accounts_client_chart_versions_firm_id_client_id_cha~",
                        columns: x => new { x.firm_id, x.client_id, x.chart_version_id },
                        principalTable: "client_chart_versions",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "accounting_capability_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    service_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    framework = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    edition = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    period_rule = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reporting_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    accounting_method = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    consolidation_method = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    review_hierarchy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    template_family = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    acceptance_state = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_capability_profiles", x => x.id);
                    table.UniqueConstraint("ak_accounting_capability_profiles_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_accounting_capability_profile_scope", "((client_id IS NOT NULL) <> (group_id IS NOT NULL)) AND length(trim(service_kind)) > 0 AND length(trim(framework)) > 0 AND reporting_currency ~ '^[A-Z]{3}$'");
                    table.ForeignKey(
                        name: "FK_accounting_capability_profiles_client_groups_firm_id_group_~",
                        columns: x => new { x.firm_id, x.group_id },
                        principalTable: "client_groups",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accounting_capability_profiles_practice_clients_firm_id_cli~",
                        columns: x => new { x.firm_id, x.client_id },
                        principalTable: "practice_clients",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "client_group_memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    control_method = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ownership_percent = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    economic_interest_percent = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_group_memberships", x => x.id);
                    table.CheckConstraint("ck_client_group_membership_values", "effective_to IS NULL OR effective_from <= effective_to AND ownership_percent >= 0 AND ownership_percent <= 100 AND economic_interest_percent >= 0 AND economic_interest_percent <= 100");
                    table.ForeignKey(
                        name: "FK_client_group_memberships_client_groups_firm_id_group_id",
                        columns: x => new { x.firm_id, x.group_id },
                        principalTable: "client_groups",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_group_memberships_practice_clients_firm_id_client_id",
                        columns: x => new { x.firm_id, x.client_id },
                        principalTable: "practice_clients",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "consolidation_scope_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    reporting_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    method = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    opening_basis = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consolidation_scope_versions", x => x.id);
                    table.UniqueConstraint("ak_consolidation_scopes_group_id", x => new { x.firm_id, x.group_id, x.id });
                    table.CheckConstraint("ck_consolidation_scope_values", "reporting_currency ~ '^[A-Z]{3}$' AND length(trim(method)) > 0 AND length(trim(opening_basis)) > 0");
                    table.ForeignKey(
                        name: "FK_consolidation_scope_versions_client_groups_firm_id_group_id",
                        columns: x => new { x.firm_id, x.group_id },
                        principalTable: "client_groups",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "group_access_grants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    granted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_access_grants", x => x.id);
                    table.ForeignKey(
                        name: "FK_group_access_grants_client_groups_firm_id_group_id",
                        columns: x => new { x.firm_id, x.group_id },
                        principalTable: "client_groups",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_group_access_grants_users_firm_id_user_id",
                        columns: x => new { x.firm_id, x.user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "client_reporting_books",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    basis = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    inclusion_rule = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_reporting_books", x => x.id);
                    table.UniqueConstraint("ak_client_reporting_books_scope_id", x => new { x.firm_id, x.client_id, x.id });
                    table.CheckConstraint("ck_client_reporting_book_values", "length(trim(code)) > 0 AND length(trim(basis)) > 0 AND length(trim(inclusion_rule)) > 0 AND currency ~ '^[A-Z]{3}$'");
                    table.ForeignKey(
                        name: "FK_client_reporting_books_client_reporting_periods_firm_id_cli~",
                        columns: x => new { x.firm_id, x.client_id, x.period_id },
                        principalTable: "client_reporting_periods",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "opening_balance_bridges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prior_period_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_package_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    prior_closing_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    current_opening_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    residual = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_opening_balance_bridges", x => x.id);
                    table.CheckConstraint("ck_opening_balance_bridge_values", "length(trim(status)) > 0");
                    table.ForeignKey(
                        name: "FK_opening_balance_bridges_client_reporting_periods_firm_id_cl~",
                        columns: x => new { x.firm_id, x.client_id, x.current_period_id },
                        principalTable: "client_reporting_periods",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exchange_rates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rate_set_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    to_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    rate_date = table.Column<DateOnly>(type: "date", nullable: false),
                    rate_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    rate = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    direction = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exchange_rates", x => x.id);
                    table.CheckConstraint("ck_exchange_rate_values", "from_currency ~ '^[A-Z]{3}$' AND to_currency ~ '^[A-Z]{3}$' AND rate > 0 AND from_currency <> to_currency");
                    table.ForeignKey(
                        name: "FK_exchange_rates_exchange_rate_set_versions_firm_id_rate_set_~",
                        columns: x => new { x.firm_id, x.rate_set_version_id },
                        principalTable: "exchange_rate_set_versions",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reporting_taxonomy_nodes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    taxonomy_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    parent_node_id = table.Column<Guid>(type: "uuid", nullable: true),
                    statement_section = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    display_sign = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    normal_balance = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    disclosure_area = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_posting = table.Column<bool>(type: "boolean", nullable: false),
                    applicability = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reporting_taxonomy_nodes", x => x.id);
                    table.UniqueConstraint("ak_reporting_taxonomy_nodes_scope_id", x => new { x.firm_id, x.taxonomy_version_id, x.id });
                    table.ForeignKey(
                        name: "FK_reporting_taxonomy_nodes_reporting_taxonomy_nodes_firm_id_t~",
                        columns: x => new { x.firm_id, x.taxonomy_version_id, x.parent_node_id },
                        principalTable: "reporting_taxonomy_nodes",
                        principalColumns: new[] { "firm_id", "taxonomy_version_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reporting_taxonomy_nodes_reporting_taxonomy_versions_firm_i~",
                        columns: x => new { x.firm_id, x.taxonomy_version_id },
                        principalTable: "reporting_taxonomy_versions",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "general_ledger_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    import_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stable_journal_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    document_number = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    posting_date = table.Column<DateOnly>(type: "date", nullable: false),
                    document_date = table.Column<DateOnly>(type: "date", nullable: true),
                    source_user = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source_system = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reversal_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    is_manual = table.Column<bool>(type: "boolean", nullable: false),
                    is_year_end = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_general_ledger_transactions", x => x.id);
                    table.UniqueConstraint("ak_gl_transactions_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.ForeignKey(
                        name: "FK_general_ledger_transactions_engagements_firm_id_client_id_e~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_general_ledger_transactions_source_import_batches_firm_id_c~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.import_batch_id },
                        principalTable: "source_import_batches",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "source_account_aliases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chart_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_system = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    alias_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    alias_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_account_aliases", x => x.id);
                    table.ForeignKey(
                        name: "FK_source_account_aliases_client_accounts_firm_id_client_id_cl~",
                        columns: x => new { x.firm_id, x.client_id, x.client_account_id },
                        principalTable: "client_accounts",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_source_account_aliases_client_chart_versions_firm_id_client~",
                        columns: x => new { x.firm_id, x.client_id, x.chart_version_id },
                        principalTable: "client_chart_versions",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "accounting_capability_acceptances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    capability_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_capability_acceptances", x => x.id);
                    table.ForeignKey(
                        name: "FK_accounting_capability_acceptances_accounting_capability_pro~",
                        columns: x => new { x.firm_id, x.capability_profile_id },
                        principalTable: "accounting_capability_profiles",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "consolidation_components",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    period_basis = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    taxonomy_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    mapping_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ownership_percent = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    control_method = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    submitted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consolidation_components", x => x.id);
                    table.UniqueConstraint("ak_consolidation_components_scope_id", x => new { x.firm_id, x.group_id, x.scope_version_id, x.id });
                    table.CheckConstraint("ck_consolidation_component_values", "package_hash ~ '^[0-9a-f]{64}$' AND currency ~ '^[A-Z]{3}$' AND ownership_percent >= 0 AND ownership_percent <= 100");
                    table.ForeignKey(
                        name: "FK_consolidation_components_client_groups_firm_id_group_id",
                        columns: x => new { x.firm_id, x.group_id },
                        principalTable: "client_groups",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_consolidation_components_consolidation_scope_versions_firm_~",
                        columns: x => new { x.firm_id, x.group_id, x.scope_version_id },
                        principalTable: "consolidation_scope_versions",
                        principalColumns: new[] { "firm_id", "group_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_consolidation_components_financial_packages_firm_id_client_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.package_id },
                        principalTable: "financial_packages",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_consolidation_components_practice_clients_firm_id_client_id",
                        columns: x => new { x.firm_id, x.client_id },
                        principalTable: "practice_clients",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "consolidation_journals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    journal_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    journal_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    total_debits = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    total_credits_abs = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consolidation_journals", x => x.id);
                    table.UniqueConstraint("ak_consolidation_journals_scope_id", x => new { x.firm_id, x.group_id, x.scope_version_id, x.id });
                    table.CheckConstraint("ck_consolidation_journal_values", "currency ~ '^[A-Z]{3}$' AND total_debits = total_credits_abs");
                    table.ForeignKey(
                        name: "FK_consolidation_journals_client_groups_firm_id_group_id",
                        columns: x => new { x.firm_id, x.group_id },
                        principalTable: "client_groups",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_consolidation_journals_consolidation_scope_versions_firm_id~",
                        columns: x => new { x.firm_id, x.group_id, x.scope_version_id },
                        principalTable: "consolidation_scope_versions",
                        principalColumns: new[] { "firm_id", "group_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "consolidation_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engine_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    input_manifest = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    run_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reporting_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    signed_total = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consolidation_runs", x => x.id);
                    table.UniqueConstraint("ak_consolidation_runs_scope_id", x => new { x.firm_id, x.group_id, x.scope_version_id, x.id });
                    table.CheckConstraint("ck_consolidation_run_values", "engine_version <> '' AND run_hash ~ '^[0-9a-f]{64}$' AND reporting_currency ~ '^[A-Z]{3}$'");
                    table.ForeignKey(
                        name: "FK_consolidation_runs_client_groups_firm_id_group_id",
                        columns: x => new { x.firm_id, x.group_id },
                        principalTable: "client_groups",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_consolidation_runs_consolidation_scope_versions_firm_id_gro~",
                        columns: x => new { x.firm_id, x.group_id, x.scope_version_id },
                        principalTable: "consolidation_scope_versions",
                        principalColumns: new[] { "firm_id", "group_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "intercompany_matches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seller_client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    buyer_client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_nature = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    period_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    transaction_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    seller_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    buyer_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    matched_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    difference = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    difference_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_intercompany_matches", x => x.id);
                    table.UniqueConstraint("ak_intercompany_matches_scope_id", x => new { x.firm_id, x.group_id, x.scope_version_id, x.id });
                    table.CheckConstraint("ck_intercompany_match_values", "length(trim(account_nature)) > 0 AND currency ~ '^[A-Z]{3}$'");
                    table.ForeignKey(
                        name: "FK_intercompany_matches_client_groups_firm_id_group_id",
                        columns: x => new { x.firm_id, x.group_id },
                        principalTable: "client_groups",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_intercompany_matches_consolidation_scope_versions_firm_id_g~",
                        columns: x => new { x.firm_id, x.group_id, x.scope_version_id },
                        principalTable: "consolidation_scope_versions",
                        principalColumns: new[] { "firm_id", "group_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_intercompany_matches_practice_clients_firm_id_buyer_client_~",
                        columns: x => new { x.firm_id, x.buyer_client_id },
                        principalTable: "practice_clients",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_intercompany_matches_practice_clients_firm_id_seller_client~",
                        columns: x => new { x.firm_id, x.seller_client_id },
                        principalTable: "practice_clients",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ownership_interest_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    child_client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    ownership_percent = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    economic_interest_percent = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    control_assessment = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    method = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ownership_interest_versions", x => x.id);
                    table.CheckConstraint("ck_ownership_interest_values", "effective_to IS NULL OR effective_from <= effective_to AND ownership_percent >= 0 AND ownership_percent <= 100 AND economic_interest_percent >= 0 AND economic_interest_percent <= 100");
                    table.ForeignKey(
                        name: "FK_ownership_interest_versions_client_groups_firm_id_group_id",
                        columns: x => new { x.firm_id, x.group_id },
                        principalTable: "client_groups",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ownership_interest_versions_consolidation_scope_versions_fi~",
                        columns: x => new { x.firm_id, x.group_id, x.scope_version_id },
                        principalTable: "consolidation_scope_versions",
                        principalColumns: new[] { "firm_id", "group_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ownership_interest_versions_practice_clients_firm_id_child_~",
                        columns: x => new { x.firm_id, x.child_client_id },
                        principalTable: "practice_clients",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ownership_interest_versions_practice_clients_firm_id_parent~",
                        columns: x => new { x.firm_id, x.parent_client_id },
                        principalTable: "practice_clients",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "general_ledger_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    import_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stable_line_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    account_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    client_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    debit = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    credit = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    original_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    original_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    functional_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    party_identifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    branch = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    cost_centre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    department = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    project = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    intercompany_counterparty = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_manual = table.Column<bool>(type: "boolean", nullable: false),
                    is_year_end = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_general_ledger_lines", x => x.id);
                    table.CheckConstraint("ck_gl_line_values", "length(trim(account_code)) > 0 AND debit >= 0 AND credit >= 0 AND NOT (debit > 0 AND credit > 0) AND original_currency ~ '^[A-Z]{3}$'");
                    table.ForeignKey(
                        name: "FK_general_ledger_lines_engagements_firm_id_client_id_engageme~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_general_ledger_lines_general_ledger_transactions_firm_id_cl~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.transaction_id },
                        principalTable: "general_ledger_transactions",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "translation_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    component_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rate_set_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    translation_policy_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    to_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    translated_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    translation_reserve = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_translation_results", x => x.id);
                    table.CheckConstraint("ck_translation_result_values", "from_currency ~ '^[A-Z]{3}$' AND to_currency ~ '^[A-Z]{3}$'");
                    table.ForeignKey(
                        name: "FK_translation_results_consolidation_components_firm_id_group_~",
                        columns: x => new { x.firm_id, x.group_id, x.scope_version_id, x.component_id },
                        principalTable: "consolidation_components",
                        principalColumns: new[] { "firm_id", "group_id", "scope_version_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_translation_results_exchange_rate_set_versions_firm_id_rate~",
                        columns: x => new { x.firm_id, x.rate_set_version_id },
                        principalTable: "exchange_rate_set_versions",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_translation_results_translation_policy_versions_firm_id_tra~",
                        columns: x => new { x.firm_id, x.translation_policy_version_id },
                        principalTable: "translation_policy_versions",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "consolidation_run_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    component_id = table.Column<Guid>(type: "uuid", nullable: true),
                    consolidation_journal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    taxonomy_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    component_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    alignment_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    elimination_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    consolidated_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consolidation_run_lines", x => x.id);
                    table.CheckConstraint("ck_consolidation_run_line_values", "length(trim(taxonomy_code)) > 0 AND currency ~ '^[A-Z]{3}$'");
                    table.ForeignKey(
                        name: "FK_consolidation_run_lines_consolidation_components_firm_id_gr~",
                        columns: x => new { x.firm_id, x.group_id, x.scope_version_id, x.component_id },
                        principalTable: "consolidation_components",
                        principalColumns: new[] { "firm_id", "group_id", "scope_version_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_consolidation_run_lines_consolidation_journals_firm_id_grou~",
                        columns: x => new { x.firm_id, x.group_id, x.scope_version_id, x.consolidation_journal_id },
                        principalTable: "consolidation_journals",
                        principalColumns: new[] { "firm_id", "group_id", "scope_version_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_consolidation_run_lines_consolidation_runs_firm_id_group_id~",
                        columns: x => new { x.firm_id, x.group_id, x.scope_version_id, x.run_id },
                        principalTable: "consolidation_runs",
                        principalColumns: new[] { "firm_id", "group_id", "scope_version_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "consolidation_journal_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consolidation_journal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    intercompany_match_id = table.Column<Guid>(type: "uuid", nullable: true),
                    taxonomy_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    debit = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    credit = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consolidation_journal_lines", x => x.id);
                    table.CheckConstraint("ck_consolidation_journal_line_values", "length(trim(taxonomy_code)) > 0 AND debit >= 0 AND credit >= 0 AND NOT (debit > 0 AND credit > 0) AND currency ~ '^[A-Z]{3}$'");
                    table.ForeignKey(
                        name: "FK_consolidation_journal_lines_consolidation_journals_firm_id_~",
                        columns: x => new { x.firm_id, x.group_id, x.scope_version_id, x.consolidation_journal_id },
                        principalTable: "consolidation_journals",
                        principalColumns: new[] { "firm_id", "group_id", "scope_version_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_consolidation_journal_lines_intercompany_matches_firm_id_gr~",
                        columns: x => new { x.firm_id, x.group_id, x.scope_version_id, x.intercompany_match_id },
                        principalTable: "intercompany_matches",
                        principalColumns: new[] { "firm_id", "group_id", "scope_version_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_accounting_capability_acceptance_stage",
                table: "accounting_capability_acceptances",
                columns: new[] { "firm_id", "capability_profile_id", "stage" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_capability_profiles_firm_id_group_id",
                table: "accounting_capability_profiles",
                columns: new[] { "firm_id", "group_id" });

            migrationBuilder.CreateIndex(
                name: "ux_accounting_capability_profile_scope_revision",
                table: "accounting_capability_profiles",
                columns: new[] { "firm_id", "client_id", "group_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_reconciliation_items_firm_id_client_id_engagemen~",
                table: "accounting_reconciliation_items",
                columns: new[] { "firm_id", "client_id", "engagement_id", "reconciliation_id" });

            migrationBuilder.CreateIndex(
                name: "ux_accounting_reconciliation_item",
                table: "accounting_reconciliation_items",
                columns: new[] { "firm_id", "reconciliation_id", "stable_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_accounting_reconciliation_area",
                table: "accounting_reconciliations",
                columns: new[] { "firm_id", "engagement_id", "period_id", "area" });

            migrationBuilder.CreateIndex(
                name: "ix_analytical_review_measure",
                table: "analytical_reviews",
                columns: new[] { "firm_id", "engagement_id", "period_id", "area", "measure" });

            migrationBuilder.CreateIndex(
                name: "IX_analytical_reviews_firm_id_client_id_engagement_id",
                table: "analytical_reviews",
                columns: new[] { "firm_id", "client_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "ux_client_accounting_profile_client",
                table: "client_accounting_profiles",
                columns: new[] { "firm_id", "client_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_accounts_firm_id_client_id_parent_account_id",
                table: "client_accounts",
                columns: new[] { "firm_id", "client_id", "parent_account_id" });

            migrationBuilder.CreateIndex(
                name: "ux_client_account_chart_code",
                table: "client_accounts",
                columns: new[] { "firm_id", "client_id", "chart_version_id", "account_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_client_account_stable_identity",
                table: "client_accounts",
                columns: new[] { "firm_id", "client_id", "stable_identity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_client_chart_version_number",
                table: "client_chart_versions",
                columns: new[] { "firm_id", "client_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_group_memberships_firm_id_client_id",
                table: "client_group_memberships",
                columns: new[] { "firm_id", "client_id" });

            migrationBuilder.CreateIndex(
                name: "ux_client_group_membership_effective",
                table: "client_group_memberships",
                columns: new[] { "firm_id", "group_id", "client_id", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_client_group_code",
                table: "client_groups",
                columns: new[] { "firm_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_client_reporting_book_identity",
                table: "client_reporting_books",
                columns: new[] { "firm_id", "client_id", "period_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_reporting_periods_firm_id_client_id_prior_period_id",
                table: "client_reporting_periods",
                columns: new[] { "firm_id", "client_id", "prior_period_id" });

            migrationBuilder.CreateIndex(
                name: "ux_client_reporting_period_identity",
                table: "client_reporting_periods",
                columns: new[] { "firm_id", "client_id", "period_code", "basis" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_consolidation_components_firm_id_client_id_engagement_id_pa~",
                table: "consolidation_components",
                columns: new[] { "firm_id", "client_id", "engagement_id", "package_id" });

            migrationBuilder.CreateIndex(
                name: "ux_consolidation_component_client",
                table: "consolidation_components",
                columns: new[] { "firm_id", "scope_version_id", "client_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_consolidation_journal_line",
                table: "consolidation_journal_lines",
                columns: new[] { "firm_id", "consolidation_journal_id", "taxonomy_code" });

            migrationBuilder.CreateIndex(
                name: "IX_consolidation_journal_lines_firm_id_group_id_scope_version_~",
                table: "consolidation_journal_lines",
                columns: new[] { "firm_id", "group_id", "scope_version_id", "consolidation_journal_id" });

            migrationBuilder.CreateIndex(
                name: "IX_consolidation_journal_lines_firm_id_group_id_scope_version~1",
                table: "consolidation_journal_lines",
                columns: new[] { "firm_id", "group_id", "scope_version_id", "intercompany_match_id" });

            migrationBuilder.CreateIndex(
                name: "ux_consolidation_journal_number",
                table: "consolidation_journals",
                columns: new[] { "firm_id", "scope_version_id", "journal_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_consolidation_run_line",
                table: "consolidation_run_lines",
                columns: new[] { "firm_id", "run_id", "taxonomy_code", "component_id", "consolidation_journal_id" });

            migrationBuilder.CreateIndex(
                name: "IX_consolidation_run_lines_firm_id_group_id_scope_version_id_~1",
                table: "consolidation_run_lines",
                columns: new[] { "firm_id", "group_id", "scope_version_id", "consolidation_journal_id" });

            migrationBuilder.CreateIndex(
                name: "IX_consolidation_run_lines_firm_id_group_id_scope_version_id_c~",
                table: "consolidation_run_lines",
                columns: new[] { "firm_id", "group_id", "scope_version_id", "component_id" });

            migrationBuilder.CreateIndex(
                name: "IX_consolidation_run_lines_firm_id_group_id_scope_version_id_r~",
                table: "consolidation_run_lines",
                columns: new[] { "firm_id", "group_id", "scope_version_id", "run_id" });

            migrationBuilder.CreateIndex(
                name: "ux_consolidation_run_hash",
                table: "consolidation_runs",
                columns: new[] { "firm_id", "scope_version_id", "run_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_consolidation_scope_version",
                table: "consolidation_scope_versions",
                columns: new[] { "firm_id", "group_id", "period_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ecl_assessments_firm_id_client_id_engagement_id_reconciliat~",
                table: "ecl_assessments",
                columns: new[] { "firm_id", "client_id", "engagement_id", "reconciliation_id" });

            migrationBuilder.CreateIndex(
                name: "ux_ecl_assessment_version",
                table: "ecl_assessments",
                columns: new[] { "firm_id", "reconciliation_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_exchange_rate_set_code",
                table: "exchange_rate_set_versions",
                columns: new[] { "firm_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_exchange_rate_identity",
                table: "exchange_rates",
                columns: new[] { "firm_id", "rate_set_version_id", "from_currency", "to_currency", "rate_date", "rate_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_general_ledger_lines_firm_id_client_id_engagement_id_transa~",
                table: "general_ledger_lines",
                columns: new[] { "firm_id", "client_id", "engagement_id", "transaction_id" });

            migrationBuilder.CreateIndex(
                name: "ux_gl_line_stable_line",
                table: "general_ledger_lines",
                columns: new[] { "firm_id", "import_batch_id", "stable_line_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_general_ledger_transactions_firm_id_client_id_engagement_id~",
                table: "general_ledger_transactions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "import_batch_id" });

            migrationBuilder.CreateIndex(
                name: "ux_gl_transaction_stable_journal",
                table: "general_ledger_transactions",
                columns: new[] { "firm_id", "import_batch_id", "stable_journal_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_group_access_grants_firm_id_user_id",
                table: "group_access_grants",
                columns: new[] { "firm_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_group_access_grant",
                table: "group_access_grants",
                columns: new[] { "firm_id", "group_id", "user_id", "role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_intercompany_matches_firm_id_buyer_client_id",
                table: "intercompany_matches",
                columns: new[] { "firm_id", "buyer_client_id" });

            migrationBuilder.CreateIndex(
                name: "IX_intercompany_matches_firm_id_seller_client_id",
                table: "intercompany_matches",
                columns: new[] { "firm_id", "seller_client_id" });

            migrationBuilder.CreateIndex(
                name: "ux_intercompany_match_identity",
                table: "intercompany_matches",
                columns: new[] { "firm_id", "scope_version_id", "seller_client_id", "buyer_client_id", "transaction_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_valuation_assessments_firm_id_client_id_engagemen~",
                table: "inventory_valuation_assessments",
                columns: new[] { "firm_id", "client_id", "engagement_id", "reconciliation_id" });

            migrationBuilder.CreateIndex(
                name: "ux_inventory_valuation_version",
                table: "inventory_valuation_assessments",
                columns: new[] { "firm_id", "reconciliation_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_journal_risk_flags_firm_id_client_id_engagement_id",
                table: "journal_risk_flags",
                columns: new[] { "firm_id", "client_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "ux_journal_risk_flag_rule",
                table: "journal_risk_flags",
                columns: new[] { "firm_id", "transaction_id", "rule_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_opening_balance_bridge_period",
                table: "opening_balance_bridges",
                columns: new[] { "firm_id", "client_id", "current_period_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ownership_interest_versions_firm_id_child_client_id",
                table: "ownership_interest_versions",
                columns: new[] { "firm_id", "child_client_id" });

            migrationBuilder.CreateIndex(
                name: "IX_ownership_interest_versions_firm_id_group_id_scope_version_~",
                table: "ownership_interest_versions",
                columns: new[] { "firm_id", "group_id", "scope_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_ownership_interest_versions_firm_id_parent_client_id",
                table: "ownership_interest_versions",
                columns: new[] { "firm_id", "parent_client_id" });

            migrationBuilder.CreateIndex(
                name: "ux_ownership_interest_pair",
                table: "ownership_interest_versions",
                columns: new[] { "firm_id", "scope_version_id", "parent_client_id", "child_client_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reporting_taxonomy_nodes_firm_id_taxonomy_version_id_parent~",
                table: "reporting_taxonomy_nodes",
                columns: new[] { "firm_id", "taxonomy_version_id", "parent_node_id" });

            migrationBuilder.CreateIndex(
                name: "ux_reporting_taxonomy_node_code",
                table: "reporting_taxonomy_nodes",
                columns: new[] { "firm_id", "taxonomy_version_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_reporting_taxonomy_code",
                table: "reporting_taxonomy_versions",
                columns: new[] { "firm_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_source_account_aliases_firm_id_client_id_client_account_id",
                table: "source_account_aliases",
                columns: new[] { "firm_id", "client_id", "client_account_id" });

            migrationBuilder.CreateIndex(
                name: "ux_source_account_alias",
                table: "source_account_aliases",
                columns: new[] { "firm_id", "client_id", "chart_version_id", "source_system", "alias_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_source_import_raw_hash",
                table: "source_import_batches",
                columns: new[] { "firm_id", "engagement_id", "raw_file_sha256_hex" },
                unique: true,
                filter: "length(raw_file_sha256_hex) > 0");

            migrationBuilder.CreateIndex(
                name: "IX_specialist_accounting_schedules_firm_id_client_id_engagemen~",
                table: "specialist_accounting_schedules",
                columns: new[] { "firm_id", "client_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_specialist_schedule_area",
                table: "specialist_accounting_schedules",
                columns: new[] { "firm_id", "engagement_id", "period_id", "area" });

            migrationBuilder.CreateIndex(
                name: "ux_translation_policy_code",
                table: "translation_policy_versions",
                columns: new[] { "firm_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_translation_results_firm_id_group_id_scope_version_id_compo~",
                table: "translation_results",
                columns: new[] { "firm_id", "group_id", "scope_version_id", "component_id" });

            migrationBuilder.CreateIndex(
                name: "IX_translation_results_firm_id_rate_set_version_id",
                table: "translation_results",
                columns: new[] { "firm_id", "rate_set_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_translation_results_firm_id_translation_policy_version_id",
                table: "translation_results",
                columns: new[] { "firm_id", "translation_policy_version_id" });

            migrationBuilder.CreateIndex(
                name: "ux_translation_result_input",
                table: "translation_results",
                columns: new[] { "firm_id", "scope_version_id", "component_id", "rate_set_version_id", "translation_policy_version_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounting_capability_acceptances");

            migrationBuilder.DropTable(
                name: "accounting_reconciliation_items");

            migrationBuilder.DropTable(
                name: "analytical_reviews");

            migrationBuilder.DropTable(
                name: "client_accounting_profiles");

            migrationBuilder.DropTable(
                name: "client_group_memberships");

            migrationBuilder.DropTable(
                name: "client_reporting_books");

            migrationBuilder.DropTable(
                name: "consolidation_journal_lines");

            migrationBuilder.DropTable(
                name: "consolidation_run_lines");

            migrationBuilder.DropTable(
                name: "ecl_assessments");

            migrationBuilder.DropTable(
                name: "exchange_rates");

            migrationBuilder.DropTable(
                name: "general_ledger_lines");

            migrationBuilder.DropTable(
                name: "group_access_grants");

            migrationBuilder.DropTable(
                name: "inventory_valuation_assessments");

            migrationBuilder.DropTable(
                name: "journal_risk_flags");

            migrationBuilder.DropTable(
                name: "opening_balance_bridges");

            migrationBuilder.DropTable(
                name: "ownership_interest_versions");

            migrationBuilder.DropTable(
                name: "reporting_taxonomy_nodes");

            migrationBuilder.DropTable(
                name: "source_account_aliases");

            migrationBuilder.DropTable(
                name: "specialist_accounting_schedules");

            migrationBuilder.DropTable(
                name: "translation_results");

            migrationBuilder.DropTable(
                name: "accounting_capability_profiles");

            migrationBuilder.DropTable(
                name: "intercompany_matches");

            migrationBuilder.DropTable(
                name: "consolidation_journals");

            migrationBuilder.DropTable(
                name: "consolidation_runs");

            migrationBuilder.DropTable(
                name: "general_ledger_transactions");

            migrationBuilder.DropTable(
                name: "accounting_reconciliations");

            migrationBuilder.DropTable(
                name: "client_reporting_periods");

            migrationBuilder.DropTable(
                name: "reporting_taxonomy_versions");

            migrationBuilder.DropTable(
                name: "client_accounts");

            migrationBuilder.DropTable(
                name: "consolidation_components");

            migrationBuilder.DropTable(
                name: "exchange_rate_set_versions");

            migrationBuilder.DropTable(
                name: "translation_policy_versions");

            migrationBuilder.DropTable(
                name: "source_import_batches");

            migrationBuilder.DropTable(
                name: "client_chart_versions");

            migrationBuilder.DropTable(
                name: "consolidation_scope_versions");

            migrationBuilder.DropTable(
                name: "client_groups");
        }
    }
}
