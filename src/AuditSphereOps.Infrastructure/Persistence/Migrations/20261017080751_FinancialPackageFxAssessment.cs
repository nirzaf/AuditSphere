using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinancialPackageFxAssessment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "financial_package_fx_assessments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    rationale = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    effect_digest = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    assessed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assessed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_package_fx_assessments", x => x.id);
                    table.UniqueConstraint("ak_financial_package_fx_assessments_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.financial_package_id, x.id });
                    table.CheckConstraint("ck_financial_package_fx_assessment_values", "assessed_by_user_id <> '00000000-0000-0000-0000-000000000000'::uuid AND state IN ('ASSESSED_ZERO','EFFECTS_RECORDED') AND length(trim(evidence_reference)) > 0 AND length(trim(rationale)) > 0 AND effect_digest ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_financial_package_fx_assessments_financial_packages_firm_id~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.financial_package_id },
                        principalTable: "financial_packages",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_financial_package_fx_assessment_preparer",
                        columns: x => new { x.firm_id, x.assessed_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_financial_package_fx_assessments_firm_id_assessed_by_user_id",
                table: "financial_package_fx_assessments",
                columns: new[] { "firm_id", "assessed_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_financial_package_fx_assessment_package",
                table: "financial_package_fx_assessments",
                columns: new[] { "firm_id", "financial_package_id" },
                unique: true);

            migrationBuilder.Sql("CREATE TRIGGER trg_financial_package_fx_assessments_append_only BEFORE UPDATE OR DELETE ON financial_package_fx_assessments FOR EACH ROW EXECUTE FUNCTION prevent_financial_statement_artifact_mutation();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_financial_package_fx_assessments_append_only ON financial_package_fx_assessments;");

            migrationBuilder.DropTable(
                name: "financial_package_fx_assessments");
        }
    }
}
