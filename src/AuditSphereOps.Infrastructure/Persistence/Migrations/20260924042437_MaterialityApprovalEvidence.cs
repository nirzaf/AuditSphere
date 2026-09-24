using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MaterialityApprovalEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "materiality_approvals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    materiality_assessment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_materiality_approvals", x => x.id);
                    table.UniqueConstraint("AK_materiality_approvals_scope_id", x => new { x.firm_id, x.client_id, x.engagement_id, x.id });
                    table.ForeignKey(
                        name: "FK_materiality_approvals_engagements_firm_id_client_id_engagem~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_materiality_approvals_materiality_assessments_firm_id_clien~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.materiality_assessment_id },
                        principalTable: "materiality_assessments",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_materiality_approvals_users_firm_id_approved_by_user_id",
                        columns: x => new { x.firm_id, x.approved_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_materiality_approvals_firm_id_approved_by_user_id",
                table: "materiality_approvals",
                columns: new[] { "firm_id", "approved_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_materiality_approvals_firm_id_client_id_engagement_id_mater~",
                table: "materiality_approvals",
                columns: new[] { "firm_id", "client_id", "engagement_id", "materiality_assessment_id" });

            migrationBuilder.CreateIndex(
                name: "ux_materiality_approval_assessment",
                table: "materiality_approvals",
                columns: new[] { "firm_id", "materiality_assessment_id" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE FUNCTION prevent_materiality_approval_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'Materiality approvals are immutable evidence.' USING ERRCODE = '55000';
                END $$;
                CREATE TRIGGER trg_materiality_approvals_immutable
                BEFORE UPDATE OR DELETE ON materiality_approvals
                FOR EACH ROW EXECUTE FUNCTION prevent_materiality_approval_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_materiality_approvals_immutable ON materiality_approvals; DROP FUNCTION IF EXISTS prevent_materiality_approval_mutation();");

            migrationBuilder.DropTable(
                name: "materiality_approvals");
        }
    }
}
