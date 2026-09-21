using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BindTrialBalanceToReportingContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tb_import_batch_values",
                table: "trial_balance_import_batches");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tb_source_layout",
                table: "trial_balance_datasets");

            migrationBuilder.AddColumn<string>(
                name: "basis",
                table: "trial_balance_import_batches",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "book_id",
                table: "trial_balance_import_batches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "period_id",
                table: "trial_balance_import_batches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "basis",
                table: "trial_balance_datasets",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "book_id",
                table: "trial_balance_datasets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "period_id",
                table: "trial_balance_datasets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_trial_balance_import_batches_firm_id_client_id_book_id",
                table: "trial_balance_import_batches",
                columns: new[] { "firm_id", "client_id", "book_id" });

            migrationBuilder.CreateIndex(
                name: "IX_trial_balance_import_batches_firm_id_client_id_period_id",
                table: "trial_balance_import_batches",
                columns: new[] { "firm_id", "client_id", "period_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_tb_import_batch_values",
                table: "trial_balance_import_batches",
                sql: "raw_file_sha256_hex ~ '^[0-9a-f]{64}$' AND normalized_dataset_digest ~ '^[0-9a-f]{64}$' AND source_layout IN ('SIGNED_NET','DEBIT_CREDIT') AND entity_count >= 2 AND status IN ('LOADING','SEALED') AND (basis IS NULL OR length(trim(basis)) > 0)");

            migrationBuilder.CreateIndex(
                name: "IX_trial_balance_datasets_firm_id_client_id_book_id",
                table: "trial_balance_datasets",
                columns: new[] { "firm_id", "client_id", "book_id" });

            migrationBuilder.CreateIndex(
                name: "IX_trial_balance_datasets_firm_id_client_id_period_id",
                table: "trial_balance_datasets",
                columns: new[] { "firm_id", "client_id", "period_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_tb_source_layout",
                table: "trial_balance_datasets",
                sql: "source_layout IN ('SIGNED_NET','DEBIT_CREDIT') AND (import_profile_version IS NOT NULL AND length(trim(import_profile_version)) > 0) AND (basis IS NULL OR length(trim(basis)) > 0)");

            migrationBuilder.AddForeignKey(
                name: "FK_trial_balance_datasets_client_reporting_books_firm_id_clien~",
                table: "trial_balance_datasets",
                columns: new[] { "firm_id", "client_id", "book_id" },
                principalTable: "client_reporting_books",
                principalColumns: new[] { "firm_id", "client_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_trial_balance_datasets_client_reporting_periods_firm_id_cli~",
                table: "trial_balance_datasets",
                columns: new[] { "firm_id", "client_id", "period_id" },
                principalTable: "client_reporting_periods",
                principalColumns: new[] { "firm_id", "client_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_trial_balance_import_batches_client_reporting_books_firm_id~",
                table: "trial_balance_import_batches",
                columns: new[] { "firm_id", "client_id", "book_id" },
                principalTable: "client_reporting_books",
                principalColumns: new[] { "firm_id", "client_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_trial_balance_import_batches_client_reporting_periods_firm_~",
                table: "trial_balance_import_batches",
                columns: new[] { "firm_id", "client_id", "period_id" },
                principalTable: "client_reporting_periods",
                principalColumns: new[] { "firm_id", "client_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_trial_balance_datasets_client_reporting_books_firm_id_clien~",
                table: "trial_balance_datasets");

            migrationBuilder.DropForeignKey(
                name: "FK_trial_balance_datasets_client_reporting_periods_firm_id_cli~",
                table: "trial_balance_datasets");

            migrationBuilder.DropForeignKey(
                name: "FK_trial_balance_import_batches_client_reporting_books_firm_id~",
                table: "trial_balance_import_batches");

            migrationBuilder.DropForeignKey(
                name: "FK_trial_balance_import_batches_client_reporting_periods_firm_~",
                table: "trial_balance_import_batches");

            migrationBuilder.DropIndex(
                name: "IX_trial_balance_import_batches_firm_id_client_id_book_id",
                table: "trial_balance_import_batches");

            migrationBuilder.DropIndex(
                name: "IX_trial_balance_import_batches_firm_id_client_id_period_id",
                table: "trial_balance_import_batches");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tb_import_batch_values",
                table: "trial_balance_import_batches");

            migrationBuilder.DropIndex(
                name: "IX_trial_balance_datasets_firm_id_client_id_book_id",
                table: "trial_balance_datasets");

            migrationBuilder.DropIndex(
                name: "IX_trial_balance_datasets_firm_id_client_id_period_id",
                table: "trial_balance_datasets");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tb_source_layout",
                table: "trial_balance_datasets");

            migrationBuilder.DropColumn(
                name: "basis",
                table: "trial_balance_import_batches");

            migrationBuilder.DropColumn(
                name: "book_id",
                table: "trial_balance_import_batches");

            migrationBuilder.DropColumn(
                name: "period_id",
                table: "trial_balance_import_batches");

            migrationBuilder.DropColumn(
                name: "basis",
                table: "trial_balance_datasets");

            migrationBuilder.DropColumn(
                name: "book_id",
                table: "trial_balance_datasets");

            migrationBuilder.DropColumn(
                name: "period_id",
                table: "trial_balance_datasets");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tb_import_batch_values",
                table: "trial_balance_import_batches",
                sql: "raw_file_sha256_hex ~ '^[0-9a-f]{64}$' AND normalized_dataset_digest ~ '^[0-9a-f]{64}$' AND source_layout IN ('SIGNED_NET','DEBIT_CREDIT') AND entity_count >= 2 AND status IN ('LOADING','SEALED')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tb_source_layout",
                table: "trial_balance_datasets",
                sql: "source_layout IN ('SIGNED_NET','DEBIT_CREDIT') AND (import_profile_version IS NOT NULL AND length(trim(import_profile_version)) > 0)");
        }
    }
}
