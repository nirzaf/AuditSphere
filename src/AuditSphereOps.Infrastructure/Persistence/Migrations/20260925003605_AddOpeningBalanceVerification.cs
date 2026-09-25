using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpeningBalanceVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "opening_balance_verifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_id = table.Column<Guid>(type: "uuid", nullable: true),
                    prior_period_id = table.Column<Guid>(type: "uuid", nullable: true),
                    prior_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    as_of_date = table.Column<DateOnly>(type: "date", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    opening_signed_total = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    agreed_signed_total = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    difference_amount = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    accounting_policies_consistent = table.Column<bool>(type: "boolean", nullable: false),
                    conclusion = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    rationale = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    evidence_references_json = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_opening_balance_verifications", x => x.id);
                    table.UniqueConstraint("AK_opening_balance_verifications_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_opening_balance_verification_values", "revision > 0 AND length(trim(prior_reference)) > 0 AND currency ~ '^[A-Z]{3}$' AND conclusion IN ('AGREED','DIFFERENCES_RESOLVED','DIFFERENCES_UNRESOLVED','NOT_VERIFIABLE')");
                    table.ForeignKey(
                        name: "FK_opening_balance_verifications_engagements_firm_id_client_id~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_opening_balance_verifications_firm_id_client_id_engagement_~",
                table: "opening_balance_verifications",
                columns: new[] { "firm_id", "client_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "ux_opening_balance_verification_engagement",
                table: "opening_balance_verifications",
                columns: new[] { "firm_id", "engagement_id", "as_of_date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "opening_balance_verifications");
        }
    }
}
