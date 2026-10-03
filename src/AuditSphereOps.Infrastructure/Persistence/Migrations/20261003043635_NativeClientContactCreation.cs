using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NativeClientContactCreation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_client_contacts_firm_id_practice_client_id",
                table: "client_contacts");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_client_contacts_firm_id_practice_client_id_id",
                table: "client_contacts",
                columns: new[] { "firm_id", "practice_client_id", "id" });

            migrationBuilder.CreateTable(
                name: "client_contact_creations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    review_basis = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    previous_generation = table.Column<long>(type: "bigint", nullable: false),
                    result_generation = table.Column<long>(type: "bigint", nullable: false),
                    input_json = table.Column<string>(type: "text", nullable: false),
                    previous_primary_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_contact_creations", x => x.id);
                    table.CheckConstraint("ck_client_contact_creation", "actor_epoch >= 1 AND previous_generation >= 1 AND result_generation = previous_generation + 1 AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND length(input_json) BETWEEN 1 AND 4000 AND length(previous_primary_json) BETWEEN 1 AND 25000");
                    table.ForeignKey(
                        name: "FK_client_contact_creations_client_contacts_firm_id_client_id_~",
                        columns: x => new { x.firm_id, x.client_id, x.contact_id },
                        principalTable: "client_contacts",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_contact_creations_practice_clients_firm_id_client_id",
                        columns: x => new { x.firm_id, x.client_id },
                        principalTable: "practice_clients",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_contact_creations_users_firm_id_actor_id",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_contact_creations_firm_id_actor_id_request_id",
                table: "client_contact_creations",
                columns: new[] { "firm_id", "actor_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_contact_creations_firm_id_client_id_contact_id",
                table: "client_contact_creations",
                columns: new[] { "firm_id", "client_id", "contact_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_contact_creations_firm_id_contact_id",
                table: "client_contact_creations",
                columns: new[] { "firm_id", "contact_id" },
                unique: true);
            migrationBuilder.Sql("""
                CREATE TRIGGER client_contact_creations_append_only BEFORE UPDATE OR DELETE ON client_contact_creations
                  FOR EACH ROW EXECUTE FUNCTION prevent_completion_evidence_mutation();
                CREATE FUNCTION enforce_client_contact_creation() RETURNS trigger AS $$
                DECLARE c client_contacts%ROWTYPE;
                BEGIN
                  SELECT * INTO c FROM client_contacts WHERE firm_id=NEW.firm_id AND practice_client_id=NEW.client_id AND id=NEW.contact_id;
                  IF c.id IS NULL OR c.full_name IS DISTINCT FROM NEW.input_json::jsonb->>'Name'
                    OR c.email IS DISTINCT FROM NEW.input_json::jsonb->>'Email'
                    OR c.role IS DISTINCT FROM NEW.input_json::jsonb->>'Role'
                    OR c.primary IS DISTINCT FROM (NEW.input_json::jsonb->>'Primary')::boolean
                    OR c.valid_from IS DISTINCT FROM NEW.created_at
                    OR NOT EXISTS(SELECT 1 FROM users WHERE firm_id=NEW.firm_id AND id=NEW.actor_id AND session_epoch=NEW.actor_epoch AND NOT disabled AND user_kind='Staff')
                    OR NOT EXISTS(SELECT 1 FROM client_safety_states WHERE firm_id=NEW.firm_id AND id=NEW.client_id AND input_generation=NEW.result_generation) THEN
                    RAISE EXCEPTION 'Contact creation receipt must match its guarded publication' USING ERRCODE='23514';
                  END IF;
                  RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER client_contact_creation_matches BEFORE INSERT ON client_contact_creations
                  FOR EACH ROW EXECUTE FUNCTION enforce_client_contact_creation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS(SELECT 1 FROM client_contact_creations) THEN
                    RAISE EXCEPTION 'Retained contact creation evidence prevents rollback';
                  END IF;
                END $$;
                DROP FUNCTION enforce_client_contact_creation() CASCADE;
                """);
            migrationBuilder.DropTable(
                name: "client_contact_creations");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_client_contacts_firm_id_practice_client_id_id",
                table: "client_contacts");

            migrationBuilder.CreateIndex(
                name: "IX_client_contacts_firm_id_practice_client_id",
                table: "client_contacts",
                columns: new[] { "firm_id", "practice_client_id" });
        }
    }
}
