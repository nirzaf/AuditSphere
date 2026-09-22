using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M365InvitationEvidenceAction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_role_grant_evidence_values",
                table: "role_grant_change_evidence");

            migrationBuilder.AddCheckConstraint(
                name: "ck_role_grant_evidence_values",
                table: "role_grant_change_evidence",
                sql: "action IN ('GRANTED','REVOKED','INVITATION_COPIED') AND length(trim(source)) > 0 AND length(trim(new_role)) > 0 AND length(trim(prior_role)) >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_role_grant_evidence_values",
                table: "role_grant_change_evidence");

            migrationBuilder.AddCheckConstraint(
                name: "ck_role_grant_evidence_values",
                table: "role_grant_change_evidence",
                sql: "action IN ('GRANTED','REVOKED') AND length(trim(source)) > 0 AND length(trim(new_role)) > 0 AND length(trim(prior_role)) >= 0");
        }
    }
}
