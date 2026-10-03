using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NativeEngagementActivationReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "actor_epoch",
                table: "engagement_activations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "engagement_generation",
                table: "engagement_activations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "request_hash",
                table: "engagement_activations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "request_id",
                table: "engagement_activations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "result_generation",
                table: "engagement_activations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "review_basis",
                table: "engagement_activations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_engagement_activations_firm_id_activated_by_user_id_request~",
                table: "engagement_activations",
                columns: new[] { "firm_id", "activated_by_user_id", "request_id" },
                unique: true,
                filter: "request_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_engagement_activation_request",
                table: "engagement_activations",
                sql: "(request_id IS NULL AND request_hash IS NULL AND review_basis IS NULL AND actor_epoch IS NULL AND engagement_generation IS NULL AND result_generation IS NULL) OR (request_id IS NOT NULL AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND request_hash IS NOT NULL AND review_basis IS NOT NULL AND actor_epoch IS NOT NULL AND engagement_generation IS NOT NULL AND result_generation IS NOT NULL AND request_hash ~ '^[a-f0-9]{64}$' AND review_basis ~ '^[a-f0-9]{64}$' AND actor_epoch >= 1 AND engagement_generation >= 1 AND result_generation > engagement_generation AND result_generation - engagement_generation = 1)");
            migrationBuilder.Sql("""
                CREATE FUNCTION enforce_native_engagement_activation() RETURNS trigger AS $$
                BEGIN
                  IF NEW.request_id IS NOT NULL AND NOT EXISTS (
                    SELECT 1 FROM engagements e
                    JOIN acceptance_decisions d ON d.id=NEW.acceptance_decision_id AND d.firm_id=NEW.firm_id
                    JOIN client_safety_states c ON c.id=e.practice_client_id AND c.firm_id=e.firm_id
                    JOIN users u ON u.id=NEW.activated_by_user_id AND u.firm_id=e.firm_id
                    WHERE e.id=NEW.engagement_id AND e.firm_id=NEW.firm_id AND e.practice_client_id=NEW.practice_client_id
                    AND e.status='Active' AND NOT e.professional_work_blocked AND e.generation=NEW.result_generation
                    AND d.practice_client_id=e.practice_client_id AND d.service_route=e.service_route AND d.decision='Accepted'
                    AND d.generation=c.input_generation AND d.generation=NEW.client_generation AND d.path=NEW.acceptance_path
                    AND u.session_epoch=NEW.actor_epoch AND NOT u.disabled
                    AND lower(u.user_kind)<>'client'
                    AND EXISTS(SELECT 1 FROM role_grants r WHERE r.firm_id=e.firm_id AND r.user_id=u.id AND lower(r.role)='partner' AND r.revoked_at IS NULL
                      AND (r.expires_at IS NULL OR r.expires_at>statement_timestamp())
                      AND ((r.client_id IS NULL AND r.engagement_id IS NULL) OR (r.client_id=e.practice_client_id AND (r.engagement_id IS NULL OR r.engagement_id=e.id))))
                    AND NOT EXISTS(SELECT 1 FROM engagement_holds h WHERE h.firm_id=e.firm_id AND h.engagement_id=e.id AND NOT h.released)
                  ) THEN RAISE EXCEPTION 'Native activation requires the exact current scoped activation and accepted decision'; END IF;
                  RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE CONSTRAINT TRIGGER native_engagement_activation_scope_guard AFTER INSERT ON engagement_activations
                  DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION enforce_native_engagement_activation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS(SELECT 1 FROM engagement_activations WHERE request_id IS NOT NULL) THEN
                    RAISE EXCEPTION 'Retained reviewed activation evidence prevents rollback';
                  END IF;
                END $$;
                DROP TRIGGER native_engagement_activation_scope_guard ON engagement_activations;
                DROP FUNCTION enforce_native_engagement_activation();
                """);
            migrationBuilder.DropIndex(
                name: "IX_engagement_activations_firm_id_activated_by_user_id_request~",
                table: "engagement_activations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_engagement_activation_request",
                table: "engagement_activations");

            migrationBuilder.DropColumn(
                name: "actor_epoch",
                table: "engagement_activations");

            migrationBuilder.DropColumn(
                name: "engagement_generation",
                table: "engagement_activations");

            migrationBuilder.DropColumn(
                name: "request_hash",
                table: "engagement_activations");

            migrationBuilder.DropColumn(
                name: "request_id",
                table: "engagement_activations");

            migrationBuilder.DropColumn(
                name: "result_generation",
                table: "engagement_activations");

            migrationBuilder.DropColumn(
                name: "review_basis",
                table: "engagement_activations");
        }
    }
}
