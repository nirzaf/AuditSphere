using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetainedLeadActivityHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lead_activity_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_backfill = table.Column<bool>(type: "boolean", nullable: false),
                    from_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    to_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    source_channel = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    opportunity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    service_route = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    proposal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    proposal_revision = table.Column<long>(type: "bigint", nullable: true),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lead_activity_events", x => x.id);
                    table.UniqueConstraint("AK_lead_activity_events_firm_id_id", x => new { x.firm_id, x.id });
                    table.CheckConstraint("ck_lead_activity_action", "action IN ('LEAD_CREATED','LEAD_QUALIFIED','OPPORTUNITY_CREATED','PROPOSAL_REVISION_CREATED','PROPOSAL_REVIEWED','PROPOSAL_MARKED_SENT','PROPOSAL_ACCEPTED','PROPOSAL_DECLINED','CLIENT_CONVERSION_LINKED')");
                    table.CheckConstraint("ck_lead_activity_actor", "actor_user_id IS NOT NULL OR is_backfill");
                    table.CheckConstraint("ck_lead_activity_backfill", "NOT is_backfill OR (action = 'LEAD_CREATED' AND actor_user_id IS NULL)");
                    table.CheckConstraint("ck_lead_activity_revision", "proposal_revision IS NULL OR proposal_revision >= 1");
                    table.CheckConstraint("ck_lead_activity_shape", "(action <> 'LEAD_CREATED' OR (to_state = 'NEW' AND source_channel IS NOT NULL)) AND (action <> 'LEAD_QUALIFIED' OR (from_state = 'NEW' AND to_state = 'QUALIFIED')) AND (action <> 'OPPORTUNITY_CREATED' OR (opportunity_id IS NOT NULL AND service_route IS NOT NULL)) AND (action <> 'PROPOSAL_REVISION_CREATED' OR (opportunity_id IS NOT NULL AND proposal_id IS NOT NULL AND proposal_revision IS NOT NULL AND to_state = 'DRAFT')) AND (action <> 'PROPOSAL_REVIEWED' OR (opportunity_id IS NOT NULL AND proposal_id IS NOT NULL AND proposal_revision IS NOT NULL AND from_state = 'DRAFT' AND to_state = 'INTERNAL_REVIEW')) AND (action <> 'PROPOSAL_MARKED_SENT' OR (opportunity_id IS NOT NULL AND proposal_id IS NOT NULL AND proposal_revision IS NOT NULL AND from_state = 'INTERNAL_REVIEW' AND to_state = 'SENT')) AND (action NOT IN ('PROPOSAL_ACCEPTED','PROPOSAL_DECLINED') OR (opportunity_id IS NOT NULL AND proposal_id IS NOT NULL AND proposal_revision IS NOT NULL AND from_state = 'SENT' AND ((action = 'PROPOSAL_ACCEPTED' AND to_state = 'ACCEPTED') OR (action = 'PROPOSAL_DECLINED' AND to_state = 'DECLINED')))) AND (action <> 'CLIENT_CONVERSION_LINKED' OR (opportunity_id IS NOT NULL AND proposal_id IS NOT NULL AND proposal_revision IS NOT NULL AND client_id IS NOT NULL AND to_state = 'PROSPECT'))");
                    table.ForeignKey(
                        name: "FK_lead_activity_events_leads_firm_id_lead_id",
                        columns: x => new { x.firm_id, x.lead_id },
                        principalTable: "leads",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lead_activity_events_opportunities_firm_id_opportunity_id",
                        columns: x => new { x.firm_id, x.opportunity_id },
                        principalTable: "opportunities",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lead_activity_events_practice_clients_firm_id_client_id",
                        columns: x => new { x.firm_id, x.client_id },
                        principalTable: "practice_clients",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lead_activity_events_proposals_firm_id_proposal_id",
                        columns: x => new { x.firm_id, x.proposal_id },
                        principalTable: "proposals",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lead_activity_events_users_firm_id_actor_user_id",
                        columns: x => new { x.firm_id, x.actor_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lead_activity_events_users_firm_id_owner_user_id",
                        columns: x => new { x.firm_id, x.owner_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_lead_activity_events_created_once",
                table: "lead_activity_events",
                columns: new[] { "firm_id", "lead_id" },
                unique: true,
                filter: "action = 'LEAD_CREATED'");

            migrationBuilder.CreateIndex(
                name: "IX_lead_activity_events_firm_id_actor_user_id",
                table: "lead_activity_events",
                columns: new[] { "firm_id", "actor_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_lead_activity_events_firm_id_client_id",
                table: "lead_activity_events",
                columns: new[] { "firm_id", "client_id" });

            migrationBuilder.CreateIndex(
                name: "IX_lead_activity_events_firm_id_opportunity_id",
                table: "lead_activity_events",
                columns: new[] { "firm_id", "opportunity_id" });

            migrationBuilder.CreateIndex(
                name: "IX_lead_activity_events_firm_id_owner_user_id",
                table: "lead_activity_events",
                columns: new[] { "firm_id", "owner_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_lead_activity_events_firm_id_proposal_id",
                table: "lead_activity_events",
                columns: new[] { "firm_id", "proposal_id" });

            migrationBuilder.CreateIndex(
                name: "IX_lead_activity_events_firm_lead_time",
                table: "lead_activity_events",
                columns: new[] { "firm_id", "lead_id", "occurred_at", "id" });

            // Preserve what is known for existing leads without inventing an actor or qualification timestamp.
            migrationBuilder.Sql("""
                INSERT INTO lead_activity_events
                  (id, firm_id, lead_id, action, occurred_at, actor_user_id, is_backfill,
                   from_state, to_state, source_channel, owner_user_id)
                SELECT gen_random_uuid(), firm_id, id, 'LEAD_CREATED', created_at, NULL, TRUE,
                       NULL, 'NEW', source, owner_user_id
                FROM leads;
                """);
            migrationBuilder.Sql("""
                CREATE FUNCTION reject_lead_activity_mutation() RETURNS trigger AS $$
                BEGIN
                  RAISE EXCEPTION 'Lead activity history is append-only.' USING ERRCODE = '55000';
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER trg_lead_activity_append_only
                  BEFORE UPDATE OR DELETE ON lead_activity_events
                  FOR EACH ROW EXECUTE FUNCTION reject_lead_activity_mutation();
                CREATE TRIGGER trg_lead_activity_no_truncate
                  BEFORE TRUNCATE ON lead_activity_events
                  FOR EACH STATEMENT EXECUTE FUNCTION reject_lead_activity_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM lead_activity_events) THEN
                    RAISE EXCEPTION 'Cannot remove retained lead activity history.' USING ERRCODE = '55000';
                  END IF;
                END $$;
                DROP TRIGGER IF EXISTS trg_lead_activity_append_only ON lead_activity_events;
                DROP TRIGGER IF EXISTS trg_lead_activity_no_truncate ON lead_activity_events;
                DROP FUNCTION IF EXISTS reject_lead_activity_mutation();
                """);
            migrationBuilder.DropTable(
                name: "lead_activity_events");
        }
    }
}
