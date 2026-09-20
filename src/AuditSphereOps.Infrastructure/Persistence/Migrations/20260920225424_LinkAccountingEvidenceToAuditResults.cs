using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinkAccountingEvidenceToAuditResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounting_evidence_audit_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    evidence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audit_procedure_result_id = table.Column<Guid>(type: "uuid", nullable: false),
                    linked_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_evidence_audit_links", x => x.id);
                    table.CheckConstraint("ck_accounting_evidence_audit_link_values", "evidence_kind IN ('ECL','INVENTORY','SPECIALIST','ANALYTICAL','JOURNAL_RISK')");
                    table.ForeignKey(
                        name: "FK_accounting_evidence_audit_links_audit_procedure_results_fir~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.audit_procedure_result_id },
                        principalTable: "audit_procedure_results",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accounting_evidence_audit_links_engagements_firm_id_client_~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accounting_evidence_audit_links_users_firm_id_linked_by_use~",
                        columns: x => new { x.firm_id, x.linked_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_evidence_audit_links_firm_id_client_id_engagemen~",
                table: "accounting_evidence_audit_links",
                columns: new[] { "firm_id", "client_id", "engagement_id", "audit_procedure_result_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_evidence_audit_links_firm_id_linked_by_user_id",
                table: "accounting_evidence_audit_links",
                columns: new[] { "firm_id", "linked_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_accounting_evidence_audit_link",
                table: "accounting_evidence_audit_links",
                columns: new[] { "firm_id", "client_id", "engagement_id", "evidence_kind", "evidence_id", "audit_procedure_result_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounting_evidence_audit_links");
        }
    }
}
