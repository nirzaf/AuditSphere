using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PbcClientCommunicationThread : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pbc_communications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pbc_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    recipient_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    portal_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    delivery_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    provider_correlation_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pbc_communications", x => x.id);
                    table.CheckConstraint("ck_pbc_communication_values", "kind IN ('REQUEST','STAFF_MESSAGE','CLIENT_MESSAGE','EMAIL') AND length(trim(body)) > 0 AND (delivery_state IS NULL OR delivery_state IN ('QUEUED','SENT','FAILED','UNCERTAIN')) AND ((kind = 'EMAIL' AND recipient_email IS NOT NULL AND subject IS NOT NULL AND portal_url IS NOT NULL AND delivery_state IS NOT NULL) OR (kind <> 'EMAIL' AND recipient_email IS NULL AND subject IS NULL AND portal_url IS NULL AND delivery_state IS NULL))");
                    table.ForeignKey(
                        name: "FK_pbc_communications_pbc_requests_firm_id_client_id_engagemen~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.pbc_request_id },
                        principalTable: "pbc_requests",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pbc_communications_users_firm_id_author_user_id",
                        columns: x => new { x.firm_id, x.author_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pbc_communications_firm_id_author_user_id",
                table: "pbc_communications",
                columns: new[] { "firm_id", "author_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_pbc_communications_firm_id_client_id_engagement_id_pbc_requ~",
                table: "pbc_communications",
                columns: new[] { "firm_id", "client_id", "engagement_id", "pbc_request_id" });

            migrationBuilder.CreateIndex(
                name: "IX_pbc_communications_firm_id_pbc_request_id_created_at",
                table: "pbc_communications",
                columns: new[] { "firm_id", "pbc_request_id", "created_at" });

            migrationBuilder.Sql("""
                CREATE FUNCTION protect_pbc_communication() RETURNS trigger AS $$
                BEGIN
                  IF TG_OP = 'DELETE' THEN
                    RAISE EXCEPTION 'PBC communication history is append-only.' USING ERRCODE = '55000';
                  END IF;
                  IF OLD.kind <> 'EMAIL' OR
                     (OLD.id, OLD.firm_id, OLD.client_id, OLD.engagement_id, OLD.pbc_request_id,
                      OLD.author_user_id, OLD.kind, OLD.body, OLD.recipient_email, OLD.subject,
                      OLD.portal_url, OLD.created_at) IS DISTINCT FROM
                     (NEW.id, NEW.firm_id, NEW.client_id, NEW.engagement_id, NEW.pbc_request_id,
                      NEW.author_user_id, NEW.kind, NEW.body, NEW.recipient_email, NEW.subject,
                      NEW.portal_url, NEW.created_at) THEN
                    RAISE EXCEPTION 'PBC communication content is immutable.' USING ERRCODE = '55000';
                  END IF;
                  RETURN NEW;
                END; $$ LANGUAGE plpgsql;
                CREATE TRIGGER trg_pbc_communication_history
                  BEFORE UPDATE OR DELETE ON pbc_communications
                  FOR EACH ROW EXECUTE FUNCTION protect_pbc_communication();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS protect_pbc_communication() CASCADE;");
            migrationBuilder.DropTable(
                name: "pbc_communications");
        }
    }
}
