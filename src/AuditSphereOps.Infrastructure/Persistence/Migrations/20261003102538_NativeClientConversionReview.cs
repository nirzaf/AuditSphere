using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NativeClientConversionReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_conversions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    review_basis = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    preview_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_conversions", x => x.id);
                    table.CheckConstraint("ck_client_conversion", "actor_epoch >= 1 AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND length(preview_json) BETWEEN 1 AND 200000");
                    table.ForeignKey(
                        name: "FK_client_conversions_practice_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "practice_clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_conversions_proposals_proposal_id",
                        column: x => x.proposal_id,
                        principalTable: "proposals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_conversions_users_firm_id_actor_id",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_conversions_client_id",
                table: "client_conversions",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "IX_client_conversions_firm_id_actor_id_request_id",
                table: "client_conversions",
                columns: new[] { "firm_id", "actor_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_conversions_proposal_id",
                table: "client_conversions",
                column: "proposal_id",
                unique: true);
            migrationBuilder.Sql("""
                -- Draft: validate names against generated migration and execute only in owned test schemas.
                CREATE TRIGGER client_conversion_immutable BEFORE UPDATE OR DELETE ON client_conversions
                 FOR EACH ROW EXECUTE FUNCTION prevent_completion_evidence_mutation();
                CREATE FUNCTION enforce_client_conversion() RETURNS trigger AS $$
                DECLARE review jsonb := NEW.preview_json::jsonb;
                BEGIN
                 IF NOT EXISTS (
                  SELECT 1 FROM proposals p
                  JOIN opportunities o ON o.id=p.opportunity_id AND o.firm_id=NEW.firm_id
                  JOIN practice_clients c ON c.id=NEW.client_id AND c.firm_id=NEW.firm_id
                  JOIN users actor ON actor.id=NEW.actor_id AND actor.firm_id=NEW.firm_id
                  WHERE p.id=NEW.proposal_id AND p.firm_id=NEW.firm_id AND p.practice_client_id=c.id
                   AND o.practice_client_id=c.id AND p.status='ACCEPTED' AND o.stage='WON'
                   AND p.revision=(review->>'ProposalRevision')::bigint
                   AND (review->>'ProposalId')::uuid=p.id AND (review->>'RequestId')::uuid=NEW.request_id
                   AND review->>'RequestHash'=NEW.request_hash AND review->>'ReviewBasis'=NEW.review_basis
                   AND o.service_route=review->>'ServiceRoute'
                   AND lower(trim(c.legal_name))=lower(trim(review->'Fields'->>'LegalName'))
                   AND ((review->>'ExistingClientId') IS NULL OR (review->>'ExistingClientId')::uuid=c.id)
                   AND actor.session_epoch=NEW.actor_epoch AND NOT actor.disabled AND lower(actor.user_kind)<>'client'
                   AND EXISTS(SELECT 1 FROM role_grants r WHERE r.firm_id=NEW.firm_id AND r.user_id=NEW.actor_id
                    AND lower(r.role) IN ('administrator','partner','manager','relationshipmanager')
                    AND r.client_id IS NULL AND r.engagement_id IS NULL AND r.revoked_at IS NULL
                    AND (r.expires_at IS NULL OR r.expires_at>statement_timestamp()))
                   AND EXISTS(SELECT 1 FROM acceptance_decisions a WHERE a.firm_id=NEW.firm_id
                    AND a.practice_client_id=c.id AND a.engagement_id IS NULL AND a.service_route=o.service_route AND a.decision='Pending')
                   AND EXISTS(SELECT 1 FROM client_portal_intents i WHERE i.firm_id=NEW.firm_id AND i.practice_client_id=c.id)
                 ) THEN RAISE EXCEPTION 'Conversion requires exact authorized committed commercial handover' USING ERRCODE='23514'; END IF;
                 RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE CONSTRAINT TRIGGER client_conversion_publication_guard AFTER INSERT ON client_conversions
                 DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION enforce_client_conversion();
                -- Down: reject retained receipts before DROP FUNCTION CASCADE / DROP TABLE.

                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS(SELECT 1 FROM client_conversions) THEN RAISE EXCEPTION 'Retained conversion evidence prevents rollback'; END IF;
                END $$;
                DROP FUNCTION enforce_client_conversion() CASCADE;
                """);
            migrationBuilder.DropTable(
                name: "client_conversions");
        }
    }
}
