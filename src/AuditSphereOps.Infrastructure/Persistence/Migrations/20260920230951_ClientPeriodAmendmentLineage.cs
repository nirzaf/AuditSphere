using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientPeriodAmendmentLineage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_period_amendments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_revision = table.Column<long>(type: "bigint", nullable: false),
                    amendment_revision = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_period_amendments", x => x.id);
                    table.CheckConstraint("ck_client_period_amendment_values", "previous_revision >= 1 AND amendment_revision = previous_revision + 1 AND length(trim(reason)) > 0");
                    table.ForeignKey(
                        name: "FK_client_period_amendments_client_reporting_periods_firm_id_c~",
                        columns: x => new { x.firm_id, x.client_id, x.period_id },
                        principalTable: "client_reporting_periods",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_period_amendments_users_firm_id_created_by_user_id",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_period_amendments_firm_id_created_by_user_id",
                table: "client_period_amendments",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_client_period_amendment_revision",
                table: "client_period_amendments",
                columns: new[] { "firm_id", "client_id", "period_id", "amendment_revision" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION enforce_client_period_amendments_append_only() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Client period amendments are immutable historical evidence.' USING ERRCODE = '55000';
                END;
                $$;
                CREATE TRIGGER trg_client_period_amendments_append_only
                BEFORE UPDATE OR DELETE ON client_period_amendments
                FOR EACH ROW EXECUTE FUNCTION enforce_client_period_amendments_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_client_period_amendments_append_only ON client_period_amendments;
                DROP FUNCTION IF EXISTS enforce_client_period_amendments_append_only();
                """);
            migrationBuilder.DropTable(
                name: "client_period_amendments");
        }
    }
}
