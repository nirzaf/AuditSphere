using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdministratorOversightAccessEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "administrator_oversight_access_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_epoch = table.Column<long>(type: "bigint", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    resource_kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: true),
                    outcome = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    authorized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_administrator_oversight_access_events", x => x.id);
                    table.CheckConstraint("ck_oversight_access_event", "session_epoch >= 1 AND length(operation) BETWEEN 1 AND 100 AND resource_kind IN ('FIRM','INVOICE','GROUP','CONSOLIDATION_SCOPE','EXTERNAL_PACK') AND ((resource_kind = 'FIRM' AND resource_id IS NULL) OR (resource_kind <> 'FIRM' AND resource_id IS NOT NULL)) AND outcome = 'DISCLOSURE_AUTHORIZED'");
                    table.ForeignKey(
                        name: "FK_administrator_oversight_access_events_users_firm_id_actor_u~",
                        columns: x => new { x.firm_id, x.actor_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_administrator_oversight_access_events_firm_id_actor_user_id",
                table: "administrator_oversight_access_events",
                columns: new[] { "firm_id", "actor_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_administrator_oversight_access_events_firm_id_authorized_at~",
                table: "administrator_oversight_access_events",
                columns: new[] { "firm_id", "authorized_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_administrator_oversight_access_events_firm_id_request_id",
                table: "administrator_oversight_access_events",
                columns: new[] { "firm_id", "request_id" },
                unique: true);
            migrationBuilder.Sql("""
                CREATE FUNCTION protect_administrator_oversight_access() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'Administrator oversight access evidence is immutable.' USING ERRCODE = '55000';
                END; $$;
                CREATE TRIGGER administrator_oversight_access_immutable BEFORE UPDATE OR DELETE
                ON administrator_oversight_access_events FOR EACH ROW EXECUTE FUNCTION protect_administrator_oversight_access();
                CREATE TRIGGER administrator_oversight_access_no_truncate BEFORE TRUNCATE
                ON administrator_oversight_access_events FOR EACH STATEMENT EXECUTE FUNCTION protect_administrator_oversight_access();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE administrator_oversight_access_events IN ACCESS EXCLUSIVE MODE;
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM administrator_oversight_access_events) THEN
                    RAISE EXCEPTION 'Cannot remove retained oversight access evidence.' USING ERRCODE = '55000';
                  END IF;
                END; $$;
                DROP TRIGGER administrator_oversight_access_immutable ON administrator_oversight_access_events;
                DROP TRIGGER administrator_oversight_access_no_truncate ON administrator_oversight_access_events;
                DROP FUNCTION protect_administrator_oversight_access();
                """);
            migrationBuilder.DropTable(
                name: "administrator_oversight_access_events");
        }
    }
}
