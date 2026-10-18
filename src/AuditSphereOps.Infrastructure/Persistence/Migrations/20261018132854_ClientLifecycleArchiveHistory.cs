using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientLifecycleArchiveHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_lifecycle_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    event_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_lifecycle_events", x => x.id);
                    table.UniqueConstraint("AK_client_lifecycle_events_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_client_lifecycle_event_kind", "event_kind IN ('ARCHIVED','RESTORED') AND revision >= 1");
                    table.ForeignKey(
                        name: "FK_client_lifecycle_events_practice_clients_firm_id_client_id",
                        columns: x => new { x.firm_id, x.client_id },
                        principalTable: "practice_clients",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_lifecycle_events_users_firm_id_actor_user_id",
                        columns: x => new { x.firm_id, x.actor_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_lifecycle_events_firm_id_actor_user_id",
                table: "client_lifecycle_events",
                columns: new[] { "firm_id", "actor_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_lifecycle_events_firm_id_client_id_occurred_at",
                table: "client_lifecycle_events",
                columns: new[] { "firm_id", "client_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_client_lifecycle_events_firm_id_client_id_revision",
                table: "client_lifecycle_events",
                columns: new[] { "firm_id", "client_id", "revision" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE FUNCTION reject_client_lifecycle_mutation() RETURNS trigger AS $$
                BEGIN
                  RAISE EXCEPTION 'Client lifecycle history is append-only.' USING ERRCODE = '55000';
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER trg_client_lifecycle_append_only
                  BEFORE UPDATE OR DELETE ON client_lifecycle_events
                  FOR EACH ROW EXECUTE FUNCTION reject_client_lifecycle_mutation();
                CREATE TRIGGER trg_client_lifecycle_no_truncate
                  BEFORE TRUNCATE ON client_lifecycle_events
                  FOR EACH STATEMENT EXECUTE FUNCTION reject_client_lifecycle_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM client_lifecycle_events) THEN
                    RAISE EXCEPTION 'Cannot remove retained client lifecycle history.' USING ERRCODE = '55000';
                  END IF;
                END $$;
                DROP TRIGGER IF EXISTS trg_client_lifecycle_append_only ON client_lifecycle_events;
                DROP TRIGGER IF EXISTS trg_client_lifecycle_no_truncate ON client_lifecycle_events;
                DROP FUNCTION IF EXISTS reject_client_lifecycle_mutation();
                """);
            migrationBuilder.DropTable(
                name: "client_lifecycle_events");
        }
    }
}
