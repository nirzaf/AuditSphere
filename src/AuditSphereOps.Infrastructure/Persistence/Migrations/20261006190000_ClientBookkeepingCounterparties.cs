using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientBookkeepingCounterparties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_bookkeeping_counterparties",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    normalized_legal_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    display_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    address = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    tax_identifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    contact_details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    payment_terms = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    default_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    external_system = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    external_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    normalized_external_identity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_bookkeeping_counterparties", x => x.id);
                    table.UniqueConstraint("AK_client_bookkeeping_counterparties_firm_id_client_id_id", x => new { x.firm_id, x.client_id, x.id });
                    table.CheckConstraint("ck_client_bookkeeping_counterparty", "length(trim(legal_name))>0 AND length(trim(display_name))>0 AND length(trim(normalized_legal_name))>0 AND role IN ('CUSTOMER','SUPPLIER','BOTH') AND country ~ '^[A-Z]{2}$' AND (default_currency='' OR default_currency ~ '^[A-Z]{3}$') AND ((external_system='' AND external_reference='' AND normalized_external_identity IS NULL) OR (length(trim(external_system))>0 AND length(trim(external_reference))>0 AND normalized_external_identity ~ '^[a-f0-9]{64}$'))");
                    table.ForeignKey(
                        name: "FK_client_bookkeeping_counterparties_practice_clients_firm_id_~",
                        columns: x => new { x.firm_id, x.client_id },
                        principalTable: "practice_clients",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_bookkeeping_counterparties_users_firm_id_created_by_~",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_bookkeeping_counterparties_firm_id_client_id_normal~1",
                table: "client_bookkeeping_counterparties",
                columns: new[] { "firm_id", "client_id", "normalized_legal_name", "country" });

            migrationBuilder.CreateIndex(
                name: "IX_client_bookkeeping_counterparties_firm_id_client_id_normali~",
                table: "client_bookkeeping_counterparties",
                columns: new[] { "firm_id", "client_id", "normalized_external_identity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_bookkeeping_counterparties_firm_id_created_by_user_id",
                table: "client_bookkeeping_counterparties",
                columns: new[] { "firm_id", "created_by_user_id" });
            migrationBuilder.Sql("""
                CREATE FUNCTION guard_client_counterparty_history() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                  RAISE EXCEPTION 'Client counterparty profiles are immutable; create a reviewed revision.' USING ERRCODE = '23514';
                END;
                $fn$;
                CREATE TRIGGER immutable_client_counterparty BEFORE UPDATE OR DELETE ON client_bookkeeping_counterparties
                  FOR EACH ROW EXECUTE FUNCTION guard_client_counterparty_history();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER immutable_client_counterparty ON client_bookkeeping_counterparties; DROP FUNCTION guard_client_counterparty_history();");
            migrationBuilder.DropTable(
                name: "client_bookkeeping_counterparties");
        }
    }
}
