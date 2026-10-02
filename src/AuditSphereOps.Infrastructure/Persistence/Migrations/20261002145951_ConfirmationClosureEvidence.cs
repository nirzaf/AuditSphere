using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConfirmationClosureEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_confirmation_closures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    confirmation_case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conclusion = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    evidence_snapshot_json = table.Column<string>(type: "text", nullable: false),
                    evidence_sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    closed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_confirmation_closures", x => x.id);
                    table.CheckConstraint("ck_audit_confirmation_closure_values", "length(trim(conclusion)) BETWEEN 1 AND 4000 AND evidence_sha256 ~ '^[0-9a-f]{64}$' AND octet_length(evidence_snapshot_json) BETWEEN 1 AND 1048576");
                    table.ForeignKey(
                        name: "FK_audit_confirmation_closures_audit_confirmation_cases_firm_i~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.confirmation_case_id },
                        principalTable: "audit_confirmation_cases",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_confirmation_closures_engagements_firm_id_client_id_e~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_confirmation_closures_users_firm_id_closed_by_user_id",
                        columns: x => new { x.firm_id, x.closed_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_confirmation_closures_firm_id_client_id_engagement_id~",
                table: "audit_confirmation_closures",
                columns: new[] { "firm_id", "client_id", "engagement_id", "confirmation_case_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_confirmation_closures_firm_id_closed_by_user_id",
                table: "audit_confirmation_closures",
                columns: new[] { "firm_id", "closed_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_confirmation_closures_firm_id_confirmation_case_id",
                table: "audit_confirmation_closures",
                columns: new[] { "firm_id", "confirmation_case_id" },
                unique: true);
            migrationBuilder.Sql(@"CREATE TRIGGER audit_confirmation_closures_append_only
              BEFORE UPDATE OR DELETE ON audit_confirmation_closures
              FOR EACH ROW EXECUTE FUNCTION prevent_completion_evidence_mutation();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_confirmation_closures");
        }
    }
}
