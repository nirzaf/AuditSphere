using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NativeValuationPreparation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "valuation_preparations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reconciliation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    evidence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    review_basis = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    input_json = table.Column<string>(type: "text", nullable: false),
                    context_json = table.Column<string>(type: "text", nullable: false),
                    result_json = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_valuation_preparations", x => x.id);
                    table.CheckConstraint("ck_valuation_preparation", "kind IN ('ECL','INVENTORY') AND actor_epoch >= 1 AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND length(trim(reason)) BETWEEN 1 AND 4000 AND length(trim(evidence_reference)) BETWEEN 1 AND 2000 AND length(input_json) BETWEEN 1 AND 20000 AND length(context_json) BETWEEN 1 AND 250000 AND length(result_json) BETWEEN 1 AND 250000");
                    table.ForeignKey(
                        name: "FK_valuation_preparations_accounting_reconciliations_firm_id_c~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.reconciliation_id },
                        principalTable: "accounting_reconciliations",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_valuation_preparations_engagements_firm_id_client_id_engage~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_valuation_preparations_users_firm_id_actor_id",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_valuation_preparations_firm_id_actor_id_request_id",
                table: "valuation_preparations",
                columns: new[] { "firm_id", "actor_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_valuation_preparations_firm_id_client_id_engagement_id_reco~",
                table: "valuation_preparations",
                columns: new[] { "firm_id", "client_id", "engagement_id", "reconciliation_id" });

            migrationBuilder.CreateIndex(
                name: "IX_valuation_preparations_firm_id_kind_evidence_id",
                table: "valuation_preparations",
                columns: new[] { "firm_id", "kind", "evidence_id" },
                unique: true);
            migrationBuilder.Sql("""
                CREATE TRIGGER valuation_preparations_append_only BEFORE UPDATE OR DELETE ON valuation_preparations
                  FOR EACH ROW EXECUTE FUNCTION prevent_completion_evidence_mutation();
                CREATE FUNCTION enforce_native_valuation_preparation() RETURNS trigger AS $$
                DECLARE target_table text; target jsonb; source accounting_reconciliations%ROWTYPE;
                BEGIN
                  target_table := CASE NEW.kind WHEN 'ECL' THEN 'ecl_assessments' WHEN 'INVENTORY' THEN 'inventory_valuation_assessments' END;
                  EXECUTE format('SELECT to_jsonb(t) FROM %I t WHERE id=$1 AND firm_id=$2 AND client_id=$3 AND engagement_id=$4 FOR SHARE',target_table)
                    INTO target USING NEW.evidence_id,NEW.firm_id,NEW.client_id,NEW.engagement_id;
                  SELECT * INTO source FROM accounting_reconciliations WHERE id=NEW.reconciliation_id
                    AND firm_id=NEW.firm_id AND client_id=NEW.client_id AND engagement_id=NEW.engagement_id FOR SHARE;
                  IF target IS NULL OR source.id IS NULL
                    OR (target->>'reconciliation_id')::uuid IS DISTINCT FROM NEW.reconciliation_id
                    OR (target->>'created_by_user_id')::uuid IS DISTINCT FROM NEW.actor_id
                    OR target->>'status' IS DISTINCT FROM 'DRAFT' OR target->>'reviewed_at' IS NOT NULL
                    OR target->>'reconciliation_source_hash' IS DISTINCT FROM source.source_hash
                    OR (target->>'input_generation')::bigint IS DISTINCT FROM source.input_generation
                    OR (NEW.context_json::jsonb->>'Id')::uuid IS DISTINCT FROM NEW.reconciliation_id
                    OR (NEW.context_json::jsonb->>'ClientId')::uuid IS DISTINCT FROM NEW.client_id
                    OR (NEW.context_json::jsonb->>'EngagementId')::uuid IS DISTINCT FROM NEW.engagement_id
                    OR (NEW.result_json::jsonb->>'Id')::uuid IS DISTINCT FROM NEW.evidence_id
                    OR NEW.result_json::jsonb->>'Kind' IS DISTINCT FROM NEW.kind
                    OR (NEW.result_json::jsonb->>'ClientId')::uuid IS DISTINCT FROM NEW.client_id
                    OR (NEW.result_json::jsonb->>'EngagementId')::uuid IS DISTINCT FROM NEW.engagement_id
                    OR (NEW.result_json::jsonb->>'CreatedByUserId')::uuid IS DISTINCT FROM NEW.actor_id THEN
                    RAISE EXCEPTION 'Native preparation requires exact retained source and scoped result';
                  END IF;
                  RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER valuation_preparations_scope_guard BEFORE INSERT ON valuation_preparations
                  FOR EACH ROW EXECUTE FUNCTION enforce_native_valuation_preparation();
                CREATE FUNCTION protect_native_valuation_inputs() RETURNS trigger AS $$
                DECLARE evidence_kind_name text;
                BEGIN
                  evidence_kind_name := CASE TG_TABLE_NAME WHEN 'ecl_assessments' THEN 'ECL' WHEN 'inventory_valuation_assessments' THEN 'INVENTORY' END;
                  IF EXISTS(SELECT 1 FROM valuation_preparations WHERE firm_id=OLD.firm_id AND evidence_id=OLD.id AND valuation_preparations.kind=evidence_kind_name) THEN
                    IF TG_OP='DELETE' OR (to_jsonb(NEW)-ARRAY['status','reviewed_by_user_id','reviewed_at'])
                      IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['status','reviewed_by_user_id','reviewed_at']) THEN
                      RAISE EXCEPTION 'Native valuation inputs are immutable; prepare a new revision';
                    END IF;
                  END IF;
                  IF TG_OP='DELETE' THEN RETURN OLD; END IF;
                  RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER native_ecl_inputs_frozen BEFORE UPDATE OR DELETE ON ecl_assessments
                  FOR EACH ROW EXECUTE FUNCTION protect_native_valuation_inputs();
                CREATE TRIGGER native_inventory_inputs_frozen BEFORE UPDATE OR DELETE ON inventory_valuation_assessments
                  FOR EACH ROW EXECUTE FUNCTION protect_native_valuation_inputs();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS(SELECT 1 FROM valuation_preparations) THEN
                    RAISE EXCEPTION 'Retained valuation preparations prevent rollback';
                  END IF;
                END $$;
                DROP TRIGGER native_ecl_inputs_frozen ON ecl_assessments;
                DROP TRIGGER native_inventory_inputs_frozen ON inventory_valuation_assessments;
                DROP FUNCTION protect_native_valuation_inputs();
                DROP FUNCTION enforce_native_valuation_preparation() CASCADE;
                """);
            migrationBuilder.DropTable(
                name: "valuation_preparations");
        }
    }
}
