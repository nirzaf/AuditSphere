using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientPeriodRestatement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_period_restatements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revised_package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_package_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revised_package_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revised_basis = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_period_restatements", x => x.id);
                    table.CheckConstraint("ck_client_period_restatement_values", "original_package_id <> revised_package_id AND original_package_hash ~ '^[0-9a-f]{64}$' AND revised_package_hash ~ '^[0-9a-f]{64}$' AND length(trim(revised_basis)) > 0 AND length(trim(reason)) > 0 AND length(trim(evidence_reference)) > 0 AND status IN ('SUBMITTED','APPROVED','REJECTED') AND ((status = 'SUBMITTED' AND approved_by_user_id IS NULL AND approved_at IS NULL) OR (status = 'APPROVED' AND approved_by_user_id IS NOT NULL AND approved_at IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_client_period_restatements_client_reporting_periods_firm_id~",
                        columns: x => new { x.firm_id, x.client_id, x.period_id },
                        principalTable: "client_reporting_periods",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_period_restatements_financial_packages_firm_id_clien~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.original_package_id },
                        principalTable: "financial_packages",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_period_restatements_financial_packages_firm_id_clie~1",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.revised_package_id },
                        principalTable: "financial_packages",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_period_restatements_firm_id_client_id_engagement_id_~",
                table: "client_period_restatements",
                columns: new[] { "firm_id", "client_id", "engagement_id", "original_package_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_period_restatements_firm_id_client_id_engagement_id~1",
                table: "client_period_restatements",
                columns: new[] { "firm_id", "client_id", "engagement_id", "revised_package_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_period_restatements_firm_id_client_id_period_id",
                table: "client_period_restatements",
                columns: new[] { "firm_id", "client_id", "period_id" });

            migrationBuilder.CreateIndex(
                name: "ux_client_period_restatement_packages",
                table: "client_period_restatements",
                columns: new[] { "firm_id", "original_package_id", "revised_package_id" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION prevent_approved_client_period_restatement_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF TG_OP = 'DELETE' OR OLD.status = 'APPROVED' THEN
                        RAISE EXCEPTION 'Approved period restatements are immutable.' USING ERRCODE = '55000';
                    END IF;
                    IF NOT (
                        NEW.id = OLD.id
                        AND NEW.firm_id = OLD.firm_id
                        AND NEW.client_id = OLD.client_id
                        AND NEW.engagement_id = OLD.engagement_id
                        AND NEW.period_id = OLD.period_id
                        AND NEW.original_package_id = OLD.original_package_id
                        AND NEW.revised_package_id = OLD.revised_package_id
                        AND NEW.original_package_hash = OLD.original_package_hash
                        AND NEW.revised_package_hash = OLD.revised_package_hash
                        AND NEW.revised_basis = OLD.revised_basis
                        AND NEW.reason = OLD.reason
                        AND NEW.evidence_reference = OLD.evidence_reference
                        AND NEW.created_by_user_id = OLD.created_by_user_id
                        AND NEW.created_at = OLD.created_at
                        AND NEW.status = 'APPROVED'
                        AND NEW.approved_by_user_id IS NOT NULL
                        AND NEW.approved_at IS NOT NULL
                    ) THEN
                        RAISE EXCEPTION 'A period restatement only permits one submitted-to-approved transition.' USING ERRCODE = '55000';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER prevent_approved_client_period_restatement_mutation
                BEFORE UPDATE OR DELETE ON client_period_restatements
                FOR EACH ROW EXECUTE FUNCTION prevent_approved_client_period_restatement_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS prevent_approved_client_period_restatement_mutation ON client_period_restatements;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS prevent_approved_client_period_restatement_mutation();");
            migrationBuilder.DropTable(
                name: "client_period_restatements");
        }
    }
}
