using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinancialPackageCashFlowScheduleAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "financial_package_cash_flow_schedule_allocations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cash_flow_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    specialist_schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    component_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source_schedule_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    signed_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_package_cash_flow_schedule_allocations", x => x.id);
                    table.CheckConstraint("ck_financial_package_cash_schedule_allocation_values", "component_code IN ('DEPRECIATION','IMPAIRMENT') AND signed_amount > 0 AND currency ~ '^[A-Z]{3}$' AND source_schedule_hash ~ '^[a-f0-9]{64}$'");
                    table.ForeignKey(
                        name: "FK_financial_package_cash_flow_schedule_allocations_financial_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.financial_package_id },
                        principalTable: "financial_packages",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cash_schedule_alloc_creator",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cash_schedule_alloc_movement_scoped",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.financial_package_id, x.cash_flow_line_id },
                        principalTable: "financial_package_cash_flow_lines",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "financial_package_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cash_schedule_alloc_schedule_scoped",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.specialist_schedule_id },
                        principalTable: "specialist_accounting_schedules",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_cash_flow_schedule_allocations_firm_id_c~1",
                table: "financial_package_cash_flow_schedule_allocations",
                columns: new[] { "firm_id", "client_id", "engagement_id", "financial_package_id", "cash_flow_line_id" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_cash_flow_schedule_allocations_firm_id_cl~",
                table: "financial_package_cash_flow_schedule_allocations",
                columns: new[] { "firm_id", "client_id", "engagement_id", "specialist_schedule_id" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_cash_flow_schedule_allocations_firm_id_cr~",
                table: "financial_package_cash_flow_schedule_allocations",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_cash_flow_schedule_allocations_firm_id_sp~",
                table: "financial_package_cash_flow_schedule_allocations",
                columns: new[] { "firm_id", "specialist_schedule_id" });

            migrationBuilder.CreateIndex(
                name: "ux_financial_package_cash_schedule_allocation_source",
                table: "financial_package_cash_flow_schedule_allocations",
                columns: new[] { "firm_id", "financial_package_id", "specialist_schedule_id", "component_code" },
                unique: true);

            migrationBuilder.Sql(@"
                CREATE FUNCTION guard_financial_package_cash_schedule_allocation_insert() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                  PERFORM 1 FROM financial_packages
                   WHERE firm_id = NEW.firm_id AND client_id = NEW.client_id
                     AND engagement_id = NEW.engagement_id AND id = NEW.financial_package_id
                   FOR UPDATE;
                  IF NOT FOUND THEN
                    RAISE EXCEPTION 'Cash-flow schedule allocation package is missing.' USING ERRCODE='23503';
                  END IF;
                  IF EXISTS (
                    SELECT 1 FROM financial_package_cash_flow_membership_seals
                     WHERE firm_id = NEW.firm_id AND client_id = NEW.client_id
                       AND engagement_id = NEW.engagement_id AND financial_package_id = NEW.financial_package_id
                  ) THEN
                    RAISE EXCEPTION 'Cash-flow source membership is sealed for this package.' USING ERRCODE='23514';
                  END IF;
                  RETURN NEW;
                END; $fn$;
                CREATE TRIGGER guard_financial_package_cash_schedule_allocation_insert
                  BEFORE INSERT ON financial_package_cash_flow_schedule_allocations
                  FOR EACH ROW EXECUTE FUNCTION guard_financial_package_cash_schedule_allocation_insert();

                CREATE FUNCTION prevent_financial_package_cash_schedule_allocation_mutation() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                  RAISE EXCEPTION 'Cash-flow schedule allocations are immutable package lineage.' USING ERRCODE='23514';
                END; $fn$;
                CREATE TRIGGER immutable_financial_package_cash_schedule_allocation
                  BEFORE UPDATE OR DELETE ON financial_package_cash_flow_schedule_allocations
                  FOR EACH ROW EXECUTE FUNCTION prevent_financial_package_cash_schedule_allocation_mutation();
                ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS immutable_financial_package_cash_schedule_allocation ON financial_package_cash_flow_schedule_allocations;
                DROP FUNCTION IF EXISTS prevent_financial_package_cash_schedule_allocation_mutation();
                DROP TRIGGER IF EXISTS guard_financial_package_cash_schedule_allocation_insert ON financial_package_cash_flow_schedule_allocations;
                DROP FUNCTION IF EXISTS guard_financial_package_cash_schedule_allocation_insert();
                ");
            migrationBuilder.DropTable(
                name: "financial_package_cash_flow_schedule_allocations");
        }
    }
}
