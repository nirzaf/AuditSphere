using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoleGrantChangeEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "role_grant_change_evidence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_grant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    prior_role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    prior_client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    prior_engagement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    new_role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    new_client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    new_engagement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_grant_change_evidence", x => x.id);
                    table.CheckConstraint("ck_role_grant_evidence_values", "action IN ('GRANTED','REVOKED') AND length(trim(source)) > 0 AND length(trim(new_role)) > 0 AND length(trim(prior_role)) >= 0");
                    table.ForeignKey(
                        name: "FK_role_grant_change_evidence_users_firm_id_target_user_id",
                        columns: x => new { x.firm_id, x.target_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_role_grant_change_evidence_firm_id_target_user_id_created_at",
                table: "role_grant_change_evidence",
                columns: new[] { "firm_id", "target_user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "role_grant_change_evidence");
        }
    }
}
