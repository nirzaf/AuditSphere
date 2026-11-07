using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RiskBandAssessmentScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_risk_band_assessments_firm_id_client_id_engagement_id_risk_~",
                table: "risk_band_assessments",
                columns: new[] { "firm_id", "client_id", "engagement_id", "risk_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_risk_band_assessment_risk_scope",
                table: "risk_band_assessments",
                columns: new[] { "firm_id", "client_id", "engagement_id", "risk_id" },
                principalTable: "audit_risks",
                principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_risk_band_assessment_risk_scope",
                table: "risk_band_assessments");

            migrationBuilder.DropIndex(
                name: "IX_risk_band_assessments_firm_id_client_id_engagement_id_risk_~",
                table: "risk_band_assessments");
        }
    }
}
