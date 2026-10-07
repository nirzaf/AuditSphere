using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientOperationalOpeningBalances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_operational_opening_balances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_revision = table.Column<long>(type: "bigint", nullable: false),
                    as_of_date = table.Column<DateOnly>(type: "date", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    evidence_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    manifest_json = table.Column<string>(type: "jsonb", nullable: false),
                    manifest_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_operational_opening_balances", x => x.id);
                    table.UniqueConstraint("AK_client_operational_opening_balances_firm_id_client_id_id", x => new { x.firm_id, x.client_id, x.id });
                    table.CheckConstraint("ck_client_operational_opening_balance", "period_revision >= 1 AND currency ~ '^[A-Z]{3}$' AND length(trim(evidence_reference)) > 0 AND evidence_sha256 ~ '^[a-f0-9]{64}$' AND jsonb_typeof(manifest_json) = 'object' AND manifest_sha256 ~ '^[a-f0-9]{64}$' AND ((approved_by_user_id IS NULL AND approved_at IS NULL) OR (approved_by_user_id IS NOT NULL AND approved_at IS NOT NULL AND approved_by_user_id <> created_by_user_id))");
                    table.ForeignKey(
                        name: "FK_client_operational_opening_balances_client_reporting_period~",
                        columns: x => new { x.firm_id, x.client_id, x.period_id },
                        principalTable: "client_reporting_periods",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_operational_opening_balances_users_firm_id_approved_~",
                        columns: x => new { x.firm_id, x.approved_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_operational_opening_balances_users_firm_id_created_b~",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_operational_opening_balances_firm_id_approved_by_use~",
                table: "client_operational_opening_balances",
                columns: new[] { "firm_id", "approved_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_operational_opening_balances_firm_id_client_id_perio~",
                table: "client_operational_opening_balances",
                columns: new[] { "firm_id", "client_id", "period_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_operational_opening_balances_firm_id_created_by_user~",
                table: "client_operational_opening_balances",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.Sql(ClientOperationalOpeningBalanceSql.Up);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ClientOperationalOpeningBalanceSql.Down);

            migrationBuilder.DropTable(
                name: "client_operational_opening_balances");
        }
    }
}
