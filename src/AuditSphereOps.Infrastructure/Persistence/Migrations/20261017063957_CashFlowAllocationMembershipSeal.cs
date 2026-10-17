using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CashFlowAllocationMembershipSeal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "financial_package_cash_flow_membership_seals",
                columns: table => new
                {
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sealed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_financial_package_cash_flow_membership_seals", x => new { x.firm_id, x.client_id, x.engagement_id, x.financial_package_id });
                    table.ForeignKey(
                        name: "FK_financial_package_cash_flow_membership_seals_financial_pack~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.financial_package_id },
                        principalTable: "financial_packages",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(@"
                INSERT INTO financial_package_cash_flow_membership_seals
                  (firm_id, client_id, engagement_id, financial_package_id, sealed_at)
                SELECT firm_id, client_id, engagement_id, id, now()
                  FROM financial_packages;

                CREATE FUNCTION guard_financial_package_cash_allocation_insert() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                  -- Serialize late inserts with package creation/sealing. Build holds this row lock until its seal is committed.
                  PERFORM 1 FROM financial_packages
                   WHERE firm_id = NEW.firm_id AND client_id = NEW.client_id
                     AND engagement_id = NEW.engagement_id AND id = NEW.financial_package_id
                   FOR UPDATE;
                  IF NOT FOUND THEN
                    RAISE EXCEPTION 'Cash-flow allocation package is missing.' USING ERRCODE='23503';
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
                CREATE TRIGGER guard_financial_package_cash_allocation_insert
                  BEFORE INSERT ON financial_package_cash_flow_allocations
                  FOR EACH ROW EXECUTE FUNCTION guard_financial_package_cash_allocation_insert();

                CREATE FUNCTION prevent_financial_package_cash_membership_seal_mutation() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                  RAISE EXCEPTION 'Cash-flow source membership seals are immutable.' USING ERRCODE='23514';
                END; $fn$;
                CREATE TRIGGER immutable_financial_package_cash_membership_seal
                  BEFORE UPDATE OR DELETE ON financial_package_cash_flow_membership_seals
                  FOR EACH ROW EXECUTE FUNCTION prevent_financial_package_cash_membership_seal_mutation();
                ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM financial_package_cash_flow_membership_seals) THEN
                    RAISE EXCEPTION 'Cash-flow membership seals are immutable historical evidence.';
                  END IF;
                END $$;
                DROP TRIGGER IF EXISTS immutable_financial_package_cash_membership_seal ON financial_package_cash_flow_membership_seals;
                DROP FUNCTION IF EXISTS prevent_financial_package_cash_membership_seal_mutation();
                DROP TRIGGER IF EXISTS guard_financial_package_cash_allocation_insert ON financial_package_cash_flow_allocations;
                DROP FUNCTION IF EXISTS guard_financial_package_cash_allocation_insert();
                ");

            migrationBuilder.DropTable(
                name: "financial_package_cash_flow_membership_seals");
        }
    }
}
