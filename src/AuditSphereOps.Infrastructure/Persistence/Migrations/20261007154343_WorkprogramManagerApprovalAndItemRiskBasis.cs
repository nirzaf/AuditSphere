using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkprogramManagerApprovalAndItemRiskBasis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "risk_basis_json",
                table: "audit_item_tests",
                type: "character varying(100000)",
                maxLength: 100000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "audit_workprogram_manager_approvals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rationale = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    input_generation = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_workprogram_manager_approvals", x => x.id);
                    table.UniqueConstraint("AK_audit_workprogram_manager_approvals_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_audit_workprogram_manager_approval_values", "input_generation > 0 AND length(trim(rationale)) > 0");
                    table.ForeignKey(
                        name: "FK_audit_workprogram_manager_approvals_engagements_firm_id_cli~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_workprogram_manager_approvals_users_firm_id_approved_~",
                        columns: x => new { x.firm_id, x.approved_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                });

            // Append-only Manager approval evidence: the retained history is immutable.
            migrationBuilder.Sql("""
              CREATE TRIGGER audit_workprogram_manager_approvals_append_only
              BEFORE UPDATE OR DELETE ON audit_workprogram_manager_approvals
              FOR EACH ROW EXECUTE FUNCTION prevent_completion_evidence_mutation();
              """);

            migrationBuilder.CreateIndex(
                name: "ix_audit_workprogram_manager_approvals_engagement_created",
                table: "audit_workprogram_manager_approvals",
                columns: new[] { "firm_id", "engagement_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_workprogram_manager_approvals_firm_id_approved_by_use~",
                table: "audit_workprogram_manager_approvals",
                columns: new[] { "firm_id", "approved_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_workprogram_manager_approvals_firm_id_client_id_engag~",
                table: "audit_workprogram_manager_approvals",
                columns: new[] { "firm_id", "client_id", "engagement_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_workprogram_manager_approvals");

            migrationBuilder.DropColumn(
                name: "risk_basis_json",
                table: "audit_item_tests");
        }
    }
}
