using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinancialPackageReviewDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "financial_package_review_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_revision = table.Column<long>(type: "bigint", nullable: false),
                    package_generation = table.Column<long>(type: "bigint", nullable: false),
                    package_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    stage = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    decision = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    evidence_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_package_review_decisions", x => x.id);
                    table.UniqueConstraint("AK_financial_package_review_decisions_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.CheckConstraint("ck_financial_package_review_values", "package_revision >= 1 AND package_generation >= 1 AND package_hash ~ '^[0-9a-f]{64}$' AND stage IN ('MANAGEMENT_APPROVAL','ACCOUNTING_REVIEW','PARTNER_APPROVAL') AND decision IN ('APPROVED','CHANGES_REQUIRED','REJECTED') AND evidence_mode IN ('SIGNED_IN','OFFLINE') AND length(trim(evidence_reference)) > 0 AND ((evidence_mode = 'SIGNED_IN' AND decided_by_user_id IS NOT NULL) OR evidence_mode = 'OFFLINE')");
                    table.ForeignKey(
                        name: "FK_financial_package_review_decisions_engagements_firm_id_clie~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_financial_package_review_decisions_financial_packages_firm_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.financial_package_id },
                        principalTable: "financial_packages",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_financial_package_review_decisions_users_firm_id_decided_by~",
                        columns: x => new { x.firm_id, x.decided_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_review_decisions_firm_id_decided_by_user_~",
                table: "financial_package_review_decisions",
                columns: new[] { "firm_id", "decided_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_financial_package_review_stage",
                table: "financial_package_review_decisions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "financial_package_id", "stage", "decided_at" });

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION enforce_financial_package_review_decisions_append_only() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Financial package review decisions are immutable historical evidence.' USING ERRCODE = '55000';
                END;
                $$;
                CREATE TRIGGER trg_financial_package_review_decisions_append_only
                BEFORE UPDATE OR DELETE ON financial_package_review_decisions
                FOR EACH ROW EXECUTE FUNCTION enforce_financial_package_review_decisions_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_financial_package_review_decisions_append_only ON financial_package_review_decisions;
                DROP FUNCTION IF EXISTS enforce_financial_package_review_decisions_append_only();
                """);
            migrationBuilder.DropTable(
                name: "financial_package_review_decisions");
        }
    }
}
