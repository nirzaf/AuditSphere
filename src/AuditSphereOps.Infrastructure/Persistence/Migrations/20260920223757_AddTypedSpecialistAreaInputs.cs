using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTypedSpecialistAreaInputs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_specialist_schedule_values",
                table: "specialist_accounting_schedules");

            migrationBuilder.AddColumn<decimal>(
                name: "equity_oci_amount",
                table: "specialist_accounting_schedules",
                type: "numeric(19,6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "equity_profit_or_loss_amount",
                table: "specialist_accounting_schedules",
                type: "numeric(19,6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "forecast_cash_input_amount",
                table: "specialist_accounting_schedules",
                type: "numeric(19,6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "forecast_debt_input_amount",
                table: "specialist_accounting_schedules",
                type: "numeric(19,6)",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "forecast_horizon_end",
                table: "specialist_accounting_schedules",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "forecast_owner",
                table: "specialist_accounting_schedules",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "forecast_sensitivity_reference",
                table: "specialist_accounting_schedules",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "forecast_sensitivity_result",
                table: "specialist_accounting_schedules",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "loan_covenant_reference",
                table: "specialist_accounting_schedules",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "loan_maturity_date",
                table: "specialist_accounting_schedules",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "loan_repayment_amount",
                table: "specialist_accounting_schedules",
                type: "numeric(19,6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payroll_bank_payment_reference",
                table: "specialist_accounting_schedules",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "payroll_contract_reference",
                table: "specialist_accounting_schedules",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "payroll_deductions_amount",
                table: "specialist_accounting_schedules",
                type: "numeric(19,6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "payroll_gross_amount",
                table: "specialist_accounting_schedules",
                type: "numeric(19,6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "payroll_net_amount",
                table: "specialist_accounting_schedules",
                type: "numeric(19,6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "related_party_disclosure_reference",
                table: "specialist_accounting_schedules",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "review_conclusion",
                table: "specialist_accounting_schedules",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "tax_base_amount",
                table: "specialist_accounting_schedules",
                type: "numeric(19,6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tax_correspondence_reference",
                table: "specialist_accounting_schedules",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "tax_jurisdiction",
                table: "specialist_accounting_schedules",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "tax_payment_evidence_reference",
                table: "specialist_accounting_schedules",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "tax_rate",
                table: "specialist_accounting_schedules",
                type: "numeric(19,6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tax_return_evidence_reference",
                table: "specialist_accounting_schedules",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "tax_rule_version",
                table: "specialist_accounting_schedules",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddCheckConstraint(
                name: "ck_specialist_schedule_values",
                table: "specialist_accounting_schedules",
                sql: "length(trim(area)) > 0 AND length(trim(methodology_version)) > 0 AND (area <> 'ASSETS' OR (length(trim(depreciation_method)) > 0 AND useful_life_months > 0)) AND (payroll_gross_amount IS NULL OR payroll_gross_amount >= 0) AND (payroll_deductions_amount IS NULL OR payroll_deductions_amount >= 0) AND (payroll_net_amount IS NULL OR payroll_net_amount >= 0) AND (loan_repayment_amount IS NULL OR loan_repayment_amount >= 0) AND (tax_base_amount IS NULL OR tax_base_amount >= 0) AND (tax_rate IS NULL OR tax_rate >= 0) AND (forecast_cash_input_amount IS NULL OR forecast_cash_input_amount >= 0) AND (forecast_debt_input_amount IS NULL OR forecast_debt_input_amount >= 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_specialist_schedule_values",
                table: "specialist_accounting_schedules");

            migrationBuilder.DropColumn(
                name: "equity_oci_amount",
                table: "specialist_accounting_schedules");

            migrationBuilder.DropColumn(
                name: "equity_profit_or_loss_amount",
                table: "specialist_accounting_schedules");

            migrationBuilder.DropColumn(
                name: "forecast_cash_input_amount",
                table: "specialist_accounting_schedules");

            migrationBuilder.DropColumn(
                name: "forecast_debt_input_amount",
                table: "specialist_accounting_schedules");

            migrationBuilder.DropColumn(
                name: "forecast_horizon_end",
                table: "specialist_accounting_schedules");

            migrationBuilder.DropColumn(
                name: "forecast_owner",
                table: "specialist_accounting_schedules");

            migrationBuilder.DropColumn(
                name: "forecast_sensitivity_reference",
                table: "specialist_accounting_schedules");

            migrationBuilder.DropColumn(
                name: "forecast_sensitivity_result",
                table: "specialist_accounting_schedules");

            migrationBuilder.DropColumn(
                name: "loan_covenant_reference",
                table: "specialist_accounting_schedules");

            migrationBuilder.DropColumn(
                name: "loan_maturity_date",
                table: "specialist_accounting_schedules");

            migrationBuilder.DropColumn(
                name: "loan_repayment_amount",
                table: "specialist_accounting_schedules");

            migrationBuilder.DropColumn(
                name: "payroll_bank_payment_reference",
                table: "specialist_accounting_schedules");

            migrationBuilder.DropColumn(
                name: "payroll_contract_reference",
                table: "specialist_accounting_schedules");

            migrationBuilder.DropColumn(
                name: "payroll_deductions_amount",
                table: "specialist_accounting_schedules");

            migrationBuilder.DropColumn(
                name: "payroll_gross_amount",
                table: "specialist_accounting_schedules");

            migrationBuilder.DropColumn(
                name: "payroll_net_amount",
                table: "specialist_accounting_schedules");

            migrationBuilder.DropColumn(
                name: "related_party_disclosure_reference",
                table: "specialist_accounting_schedules");

            migrationBuilder.DropColumn(
                name: "review_conclusion",
                table: "specialist_accounting_schedules");

            migrationBuilder.DropColumn(
                name: "tax_base_amount",
                table: "specialist_accounting_schedules");

            migrationBuilder.DropColumn(
                name: "tax_correspondence_reference",
                table: "specialist_accounting_schedules");

            migrationBuilder.DropColumn(
                name: "tax_jurisdiction",
                table: "specialist_accounting_schedules");

            migrationBuilder.DropColumn(
                name: "tax_payment_evidence_reference",
                table: "specialist_accounting_schedules");

            migrationBuilder.DropColumn(
                name: "tax_rate",
                table: "specialist_accounting_schedules");

            migrationBuilder.DropColumn(
                name: "tax_return_evidence_reference",
                table: "specialist_accounting_schedules");

            migrationBuilder.DropColumn(
                name: "tax_rule_version",
                table: "specialist_accounting_schedules");

            migrationBuilder.AddCheckConstraint(
                name: "ck_specialist_schedule_values",
                table: "specialist_accounting_schedules",
                sql: "length(trim(area)) > 0 AND length(trim(methodology_version)) > 0 AND (area <> 'ASSETS' OR (length(trim(depreciation_method)) > 0 AND useful_life_months > 0))");
        }
    }
}
