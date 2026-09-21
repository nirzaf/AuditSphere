using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReconcileGeneralLedgerOpeningMovement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_general_ledger_completeness_bridges_trial_balance_datasets_~",
                table: "general_ledger_completeness_bridges");

            migrationBuilder.AddColumn<decimal>(
                name: "closing_amount",
                table: "general_ledger_completeness_bridges",
                type: "numeric(19,6)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "completeness_disclosure",
                table: "general_ledger_completeness_bridges",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "incomplete_extract",
                table: "general_ledger_completeness_bridges",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "journal_exception_count",
                table: "general_ledger_completeness_bridges",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "movement_amount",
                table: "general_ledger_completeness_bridges",
                type: "numeric(19,6)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "opening_amount",
                table: "general_ledger_completeness_bridges",
                type: "numeric(19,6)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "opening_movement_mismatched_account_count",
                table: "general_ledger_completeness_bridges",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "opening_movement_residual",
                table: "general_ledger_completeness_bridges",
                type: "numeric(19,6)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "opening_movement_residual_digest",
                table: "general_ledger_completeness_bridges",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "opening_trial_balance_dataset_id",
                table: "general_ledger_completeness_bridges",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "opening_trial_balance_hash",
                table: "general_ledger_completeness_bridges",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_general_ledger_completeness_bridges_firm_id_client_id_enga~1",
                table: "general_ledger_completeness_bridges",
                columns: new[] { "firm_id", "client_id", "engagement_id", "opening_trial_balance_dataset_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_general_ledger_completeness_bridges_trial_balance_datasets_~",
                table: "general_ledger_completeness_bridges",
                columns: new[] { "firm_id", "client_id", "engagement_id", "opening_trial_balance_dataset_id" },
                principalTable: "trial_balance_datasets",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_general_ledger_completeness_bridges_trial_balance_datasets~1",
                table: "general_ledger_completeness_bridges",
                columns: new[] { "firm_id", "client_id", "engagement_id", "trial_balance_dataset_id" },
                principalTable: "trial_balance_datasets",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_general_ledger_completeness_bridges_trial_balance_datasets_~",
                table: "general_ledger_completeness_bridges");

            migrationBuilder.DropForeignKey(
                name: "FK_general_ledger_completeness_bridges_trial_balance_datasets~1",
                table: "general_ledger_completeness_bridges");

            migrationBuilder.DropIndex(
                name: "IX_general_ledger_completeness_bridges_firm_id_client_id_enga~1",
                table: "general_ledger_completeness_bridges");

            migrationBuilder.DropColumn(
                name: "closing_amount",
                table: "general_ledger_completeness_bridges");

            migrationBuilder.DropColumn(
                name: "completeness_disclosure",
                table: "general_ledger_completeness_bridges");

            migrationBuilder.DropColumn(
                name: "incomplete_extract",
                table: "general_ledger_completeness_bridges");

            migrationBuilder.DropColumn(
                name: "journal_exception_count",
                table: "general_ledger_completeness_bridges");

            migrationBuilder.DropColumn(
                name: "movement_amount",
                table: "general_ledger_completeness_bridges");

            migrationBuilder.DropColumn(
                name: "opening_amount",
                table: "general_ledger_completeness_bridges");

            migrationBuilder.DropColumn(
                name: "opening_movement_mismatched_account_count",
                table: "general_ledger_completeness_bridges");

            migrationBuilder.DropColumn(
                name: "opening_movement_residual",
                table: "general_ledger_completeness_bridges");

            migrationBuilder.DropColumn(
                name: "opening_movement_residual_digest",
                table: "general_ledger_completeness_bridges");

            migrationBuilder.DropColumn(
                name: "opening_trial_balance_dataset_id",
                table: "general_ledger_completeness_bridges");

            migrationBuilder.DropColumn(
                name: "opening_trial_balance_hash",
                table: "general_ledger_completeness_bridges");

            migrationBuilder.AddForeignKey(
                name: "FK_general_ledger_completeness_bridges_trial_balance_datasets_~",
                table: "general_ledger_completeness_bridges",
                columns: new[] { "firm_id", "client_id", "engagement_id", "trial_balance_dataset_id" },
                principalTable: "trial_balance_datasets",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
