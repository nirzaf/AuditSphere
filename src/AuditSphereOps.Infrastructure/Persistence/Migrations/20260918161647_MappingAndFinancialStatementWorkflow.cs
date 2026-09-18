using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MappingAndFinancialStatementWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The original financial package table was a placeholder and does not
            // contain enough identity to infer a mapping, plan, or calculation hash.
            // Refuse ambiguous history instead of manufacturing defaults.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM financial_packages) THEN
                    RAISE EXCEPTION 'Financial statement migration requires an explicit disposition for existing financial package rows.';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropIndex(
                name: "IX_trial_balance_datasets_firm_id_client_id",
                table: "trial_balance_datasets");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "financial_packages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "currency",
                table: "financial_packages",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<Guid>(
                name: "adjustment_plan_id",
                table: "financial_packages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "calculation_engine_version",
                table: "financial_packages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "calculation_hash",
                table: "financial_packages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "framework",
                table: "financial_packages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "mapping_version_id",
                table: "financial_packages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "period_end",
                table: "financial_packages",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "period_start",
                table: "financial_packages",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "taxonomy_version",
                table: "financial_packages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "template_version",
                table: "financial_packages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_trial_balance_datasets_scope_id",
                table: "trial_balance_datasets",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_financial_packages_firm_id_id",
                table: "financial_packages",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_financial_packages_scope_id",
                table: "financial_packages",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_adjustment_plans_firm_id_id",
                table: "adjustment_plans",
                columns: new[] { "firm_id", "id" });

            migrationBuilder.CreateTable(
                name: "adjusted_tb_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    base_dataset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    adjustment_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    result_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_adjusted_tb_snapshots", x => x.id);
                    table.UniqueConstraint("AK_adjusted_tb_snapshots_firm_id_id", x => new { x.firm_id, x.id });
                    table.UniqueConstraint("AK_adjusted_tb_snapshots_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.CheckConstraint("ck_adjusted_tb_snapshot_values", "revision >= 1 AND currency ~ '^[A-Z]{3}$' AND result_hash ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_adjusted_tb_snapshots_adjustment_plans_firm_id_adjustment_p~",
                        columns: x => new { x.firm_id, x.adjustment_plan_id },
                        principalTable: "adjustment_plans",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_adjusted_tb_snapshots_engagements_firm_id_client_id_engagem~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_adjusted_tb_snapshots_practice_clients_firm_id_client_id",
                        columns: x => new { x.firm_id, x.client_id },
                        principalTable: "practice_clients",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_adjusted_tb_snapshots_trial_balance_datasets_firm_id_client~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.base_dataset_id },
                        principalTable: "trial_balance_datasets",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_adjusted_tb_snapshots_users_firm_id_created_by_user_id",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "financial_package_validations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    passed = table.Column<bool>(type: "boolean", nullable: false),
                    detail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_package_validations", x => x.id);
                    table.CheckConstraint("ck_financial_package_validation_values", "length(code) > 0 AND length(detail) > 0");
                    table.ForeignKey(
                        name: "FK_financial_package_validations_financial_packages_firm_id_fi~",
                        columns: x => new { x.firm_id, x.financial_package_id },
                        principalTable: "financial_packages",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mapping_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dataset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    generation = table.Column<long>(type: "bigint", nullable: false),
                    taxonomy_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    period_start = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    period_end = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mapping_versions", x => x.id);
                    table.UniqueConstraint("AK_mapping_versions_firm_id_id", x => new { x.firm_id, x.id });
                    table.UniqueConstraint("AK_mapping_versions_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.CheckConstraint("ck_mapping_version_values", "version >= 1 AND generation >= 1 AND length(taxonomy_version) > 0 AND length(period_start) = 10 AND length(period_end) = 10 AND period_start <= period_end AND status IN ('DRAFT','APPROVED') AND ((status = 'DRAFT' AND approved_by_user_id IS NULL AND approved_at IS NULL) OR (status = 'APPROVED' AND approved_by_user_id IS NOT NULL AND approved_at IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_mapping_versions_engagements_firm_id_client_id_engagement_id",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_mapping_versions_practice_clients_firm_id_client_id",
                        columns: x => new { x.firm_id, x.client_id },
                        principalTable: "practice_clients",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_mapping_versions_trial_balance_datasets_firm_id_client_id_e~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.dataset_id },
                        principalTable: "trial_balance_datasets",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_mapping_versions_users_firm_id_approved_by_user_id",
                        columns: x => new { x.firm_id, x.approved_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_mapping_versions_users_firm_id_created_by_user_id",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "adjusted_tb_rows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_adjusted_tb_rows", x => x.id);
                    table.CheckConstraint("ck_adjusted_tb_row_values", "length(account_code) > 0 AND currency ~ '^[A-Z]{3}$'");
                    table.ForeignKey(
                        name: "FK_adjusted_tb_rows_adjusted_tb_snapshots_snapshot_id",
                        column: x => x.snapshot_id,
                        principalTable: "adjusted_tb_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "financial_package_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_account_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    destination_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    statement_section = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    fraction = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    adjusted_snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_package_lines", x => x.id);
                    table.CheckConstraint("ck_financial_package_line_values", "length(source_account_code) > 0 AND length(destination_code) > 0 AND length(statement_section) > 0 AND fraction > 0 AND fraction <= 1 AND currency ~ '^[A-Z]{3}$'");
                    table.ForeignKey(
                        name: "FK_financial_package_lines_adjusted_tb_snapshots_firm_id_clien~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.adjusted_snapshot_id },
                        principalTable: "adjusted_tb_snapshots",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_financial_package_lines_financial_packages_firm_id_client_i~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.financial_package_id },
                        principalTable: "financial_packages",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mapping_allocations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mapping_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_account_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    destination_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    statement_section = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    audit_area = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    fraction = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    rationale = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mapping_allocations", x => x.id);
                    table.CheckConstraint("ck_mapping_allocation_values", "length(source_account_code) > 0 AND length(destination_code) > 0 AND length(statement_section) > 0 AND fraction > 0 AND fraction <= 1 AND length(rationale) > 0");
                    table.ForeignKey(
                        name: "FK_mapping_allocations_mapping_versions_firm_id_client_id_enga~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.mapping_version_id },
                        principalTable: "mapping_versions",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_financial_packages_firm_id_client_id_engagement_id_adjusted~",
                table: "financial_packages",
                columns: new[] { "firm_id", "client_id", "engagement_id", "adjusted_dataset_id" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_packages_firm_id_client_id_engagement_id_mapping_~",
                table: "financial_packages",
                columns: new[] { "firm_id", "client_id", "engagement_id", "mapping_version_id" });

            migrationBuilder.CreateIndex(
                name: "ux_financial_package_identity",
                table: "financial_packages",
                columns: new[] { "firm_id", "adjustment_plan_id", "mapping_version_id", "template_version" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_financial_package_values",
                table: "financial_packages",
                sql: "revision >= 1 AND generation >= 1 AND length(framework) > 0 AND length(period_start) = 10 AND length(period_end) = 10 AND period_start <= period_end AND length(taxonomy_version) > 0 AND length(template_version) > 0 AND length(calculation_engine_version) > 0 AND calculation_hash ~ '^[0-9a-f]{64}$' AND currency ~ '^[A-Z]{3}$' AND status IN ('REVIEW_REQUIRED','VALIDATED')");

            migrationBuilder.CreateIndex(
                name: "ux_adjusted_tb_row_account",
                table: "adjusted_tb_rows",
                columns: new[] { "snapshot_id", "account_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_adjusted_tb_snapshots_firm_id_client_id_engagement_id_base_~",
                table: "adjusted_tb_snapshots",
                columns: new[] { "firm_id", "client_id", "engagement_id", "base_dataset_id" });

            migrationBuilder.CreateIndex(
                name: "IX_adjusted_tb_snapshots_firm_id_created_by_user_id",
                table: "adjusted_tb_snapshots",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_adjusted_tb_snapshot_plan",
                table: "adjusted_tb_snapshots",
                columns: new[] { "firm_id", "adjustment_plan_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_lines_firm_id_client_id_engagement_id_adj~",
                table: "financial_package_lines",
                columns: new[] { "firm_id", "client_id", "engagement_id", "adjusted_snapshot_id" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_lines_firm_id_client_id_engagement_id_fin~",
                table: "financial_package_lines",
                columns: new[] { "firm_id", "client_id", "engagement_id", "financial_package_id" });

            migrationBuilder.CreateIndex(
                name: "ux_financial_package_line_identity",
                table: "financial_package_lines",
                columns: new[] { "firm_id", "financial_package_id", "source_account_code", "destination_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_financial_package_validation_code",
                table: "financial_package_validations",
                columns: new[] { "firm_id", "financial_package_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mapping_allocations_firm_id_client_id_engagement_id_mapping~",
                table: "mapping_allocations",
                columns: new[] { "firm_id", "client_id", "engagement_id", "mapping_version_id" });

            migrationBuilder.CreateIndex(
                name: "ux_mapping_allocation_identity",
                table: "mapping_allocations",
                columns: new[] { "firm_id", "mapping_version_id", "source_account_code", "destination_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mapping_versions_firm_id_approved_by_user_id",
                table: "mapping_versions",
                columns: new[] { "firm_id", "approved_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_mapping_versions_firm_id_client_id_engagement_id_dataset_id",
                table: "mapping_versions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "dataset_id" });

            migrationBuilder.CreateIndex(
                name: "IX_mapping_versions_firm_id_created_by_user_id",
                table: "mapping_versions",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_mapping_version_identity",
                table: "mapping_versions",
                columns: new[] { "firm_id", "engagement_id", "dataset_id", "version" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_financial_packages_adjusted_tb_snapshots_firm_id_client_id_~",
                table: "financial_packages",
                columns: new[] { "firm_id", "client_id", "engagement_id", "adjusted_dataset_id" },
                principalTable: "adjusted_tb_snapshots",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_financial_packages_adjustment_plans_firm_id_adjustment_plan~",
                table: "financial_packages",
                columns: new[] { "firm_id", "adjustment_plan_id" },
                principalTable: "adjustment_plans",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_financial_packages_engagements_firm_id_client_id_engagement~",
                table: "financial_packages",
                columns: new[] { "firm_id", "client_id", "engagement_id" },
                principalTable: "engagements",
                principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_financial_packages_mapping_versions_firm_id_client_id_engag~",
                table: "financial_packages",
                columns: new[] { "firm_id", "client_id", "engagement_id", "mapping_version_id" },
                principalTable: "mapping_versions",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_financial_packages_practice_clients_firm_id_client_id",
                table: "financial_packages",
                columns: new[] { "firm_id", "client_id" },
                principalTable: "practice_clients",
                principalColumns: new[] { "firm_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION prevent_financial_statement_artifact_mutation() RETURNS trigger AS $$
                BEGIN
                  RAISE EXCEPTION '% is append-only; corrections require a new version.', TG_TABLE_NAME USING ERRCODE = '55000', HINT = 'Create a replacement artifact instead of mutating historical evidence.';
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_mapping_allocations_append_only
                  BEFORE UPDATE OR DELETE ON mapping_allocations
                  FOR EACH ROW EXECUTE FUNCTION prevent_financial_statement_artifact_mutation();
                CREATE TRIGGER trg_adjusted_tb_snapshots_append_only
                  BEFORE UPDATE OR DELETE ON adjusted_tb_snapshots
                  FOR EACH ROW EXECUTE FUNCTION prevent_financial_statement_artifact_mutation();
                CREATE TRIGGER trg_adjusted_tb_rows_append_only
                  BEFORE UPDATE OR DELETE ON adjusted_tb_rows
                  FOR EACH ROW EXECUTE FUNCTION prevent_financial_statement_artifact_mutation();
                CREATE TRIGGER trg_financial_packages_append_only
                  BEFORE UPDATE OR DELETE ON financial_packages
                  FOR EACH ROW EXECUTE FUNCTION prevent_financial_statement_artifact_mutation();
                CREATE TRIGGER trg_financial_package_lines_append_only
                  BEFORE UPDATE OR DELETE ON financial_package_lines
                  FOR EACH ROW EXECUTE FUNCTION prevent_financial_statement_artifact_mutation();
                CREATE TRIGGER trg_financial_package_validations_append_only
                  BEFORE UPDATE OR DELETE ON financial_package_validations
                  FOR EACH ROW EXECUTE FUNCTION prevent_financial_statement_artifact_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM mapping_allocations)
                     OR EXISTS (SELECT 1 FROM adjusted_tb_snapshots)
                     OR EXISTS (SELECT 1 FROM adjusted_tb_rows)
                     OR EXISTS (SELECT 1 FROM financial_packages)
                     OR EXISTS (SELECT 1 FROM financial_package_lines)
                     OR EXISTS (SELECT 1 FROM financial_package_validations) THEN
                    RAISE EXCEPTION 'Financial statement downgrade would discard immutable accounting evidence.';
                  END IF;
                END $$;
                """);
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_mapping_allocations_append_only ON mapping_allocations;
                DROP TRIGGER IF EXISTS trg_adjusted_tb_snapshots_append_only ON adjusted_tb_snapshots;
                DROP TRIGGER IF EXISTS trg_adjusted_tb_rows_append_only ON adjusted_tb_rows;
                DROP TRIGGER IF EXISTS trg_financial_packages_append_only ON financial_packages;
                DROP TRIGGER IF EXISTS trg_financial_package_lines_append_only ON financial_package_lines;
                DROP TRIGGER IF EXISTS trg_financial_package_validations_append_only ON financial_package_validations;
                DROP FUNCTION IF EXISTS prevent_financial_statement_artifact_mutation();
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_financial_packages_adjusted_tb_snapshots_firm_id_client_id_~",
                table: "financial_packages");

            migrationBuilder.DropForeignKey(
                name: "FK_financial_packages_adjustment_plans_firm_id_adjustment_plan~",
                table: "financial_packages");

            migrationBuilder.DropForeignKey(
                name: "FK_financial_packages_engagements_firm_id_client_id_engagement~",
                table: "financial_packages");

            migrationBuilder.DropForeignKey(
                name: "FK_financial_packages_mapping_versions_firm_id_client_id_engag~",
                table: "financial_packages");

            migrationBuilder.DropForeignKey(
                name: "FK_financial_packages_practice_clients_firm_id_client_id",
                table: "financial_packages");

            migrationBuilder.DropTable(
                name: "adjusted_tb_rows");

            migrationBuilder.DropTable(
                name: "financial_package_lines");

            migrationBuilder.DropTable(
                name: "financial_package_validations");

            migrationBuilder.DropTable(
                name: "mapping_allocations");

            migrationBuilder.DropTable(
                name: "adjusted_tb_snapshots");

            migrationBuilder.DropTable(
                name: "mapping_versions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_trial_balance_datasets_scope_id",
                table: "trial_balance_datasets");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_financial_packages_firm_id_id",
                table: "financial_packages");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_financial_packages_scope_id",
                table: "financial_packages");

            migrationBuilder.DropIndex(
                name: "IX_financial_packages_firm_id_client_id_engagement_id_adjusted~",
                table: "financial_packages");

            migrationBuilder.DropIndex(
                name: "IX_financial_packages_firm_id_client_id_engagement_id_mapping_~",
                table: "financial_packages");

            migrationBuilder.DropIndex(
                name: "ux_financial_package_identity",
                table: "financial_packages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_financial_package_values",
                table: "financial_packages");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_adjustment_plans_firm_id_id",
                table: "adjustment_plans");

            migrationBuilder.DropColumn(
                name: "adjustment_plan_id",
                table: "financial_packages");

            migrationBuilder.DropColumn(
                name: "calculation_engine_version",
                table: "financial_packages");

            migrationBuilder.DropColumn(
                name: "calculation_hash",
                table: "financial_packages");

            migrationBuilder.DropColumn(
                name: "framework",
                table: "financial_packages");

            migrationBuilder.DropColumn(
                name: "mapping_version_id",
                table: "financial_packages");

            migrationBuilder.DropColumn(
                name: "period_end",
                table: "financial_packages");

            migrationBuilder.DropColumn(
                name: "period_start",
                table: "financial_packages");

            migrationBuilder.DropColumn(
                name: "taxonomy_version",
                table: "financial_packages");

            migrationBuilder.DropColumn(
                name: "template_version",
                table: "financial_packages");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "financial_packages",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "currency",
                table: "financial_packages",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3);

            migrationBuilder.CreateIndex(
                name: "IX_trial_balance_datasets_firm_id_client_id",
                table: "trial_balance_datasets",
                columns: new[] { "firm_id", "client_id" });
        }
    }
}
