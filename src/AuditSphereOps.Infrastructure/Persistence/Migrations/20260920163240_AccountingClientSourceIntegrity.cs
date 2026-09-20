using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountingClientSourceIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_dataset_firm_engagement_hash",
                table: "trial_balance_datasets");

            migrationBuilder.DropCheckConstraint(
                name: "ck_mapping_allocation_values",
                table: "mapping_allocations");

            migrationBuilder.AddColumn<string>(
                name: "legal_entity_key",
                table: "trial_balance_datasets",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "normalized_dataset_digest",
                table: "trial_balance_datasets",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "raw_file_sha256_hex",
                table: "trial_balance_datasets",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "residual_policy",
                table: "mapping_allocations",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "LAST_DESTINATION");

            migrationBuilder.AddColumn<decimal>(
                name: "rounding_residual",
                table: "financial_package_lines",
                type: "numeric(19,6)",
                precision: 19,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "ux_dataset_firm_engagement_raw_hash",
                table: "trial_balance_datasets",
                columns: new[] { "firm_id", "engagement_id", "raw_file_sha256_hex" },
                unique: true,
                filter: "length(raw_file_sha256_hex) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_mapping_allocation_values",
                table: "mapping_allocations",
                sql: "length(source_account_code) > 0 AND length(destination_code) > 0 AND length(statement_section) > 0 AND fraction > 0 AND fraction <= 1 AND length(rationale) > 0 AND residual_policy = 'LAST_DESTINATION'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_dataset_firm_engagement_raw_hash",
                table: "trial_balance_datasets");

            migrationBuilder.DropCheckConstraint(
                name: "ck_mapping_allocation_values",
                table: "mapping_allocations");

            migrationBuilder.DropColumn(
                name: "legal_entity_key",
                table: "trial_balance_datasets");

            migrationBuilder.DropColumn(
                name: "normalized_dataset_digest",
                table: "trial_balance_datasets");

            migrationBuilder.DropColumn(
                name: "raw_file_sha256_hex",
                table: "trial_balance_datasets");

            migrationBuilder.DropColumn(
                name: "residual_policy",
                table: "mapping_allocations");

            migrationBuilder.DropColumn(
                name: "rounding_residual",
                table: "financial_package_lines");

            migrationBuilder.CreateIndex(
                name: "ux_dataset_firm_engagement_hash",
                table: "trial_balance_datasets",
                columns: new[] { "firm_id", "engagement_id", "sha256_hex" },
                unique: true,
                filter: "length(sha256_hex) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_mapping_allocation_values",
                table: "mapping_allocations",
                sql: "length(source_account_code) > 0 AND length(destination_code) > 0 AND length(statement_section) > 0 AND fraction > 0 AND fraction <= 1 AND length(rationale) > 0");
        }
    }
}
