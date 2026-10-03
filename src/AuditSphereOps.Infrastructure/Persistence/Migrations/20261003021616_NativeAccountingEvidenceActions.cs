using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NativeAccountingEvidenceActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_accounting_evidence_audit_links_firm_id_client_id_engagemen~",
                table: "accounting_evidence_audit_links",
                columns: new[] { "firm_id", "client_id", "engagement_id", "id" });

            migrationBuilder.CreateTable(
                name: "accounting_evidence_actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    review_basis = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    result_id = table.Column<Guid>(type: "uuid", nullable: true),
                    link_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decision = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    before_json = table.Column<string>(type: "text", nullable: false),
                    after_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_evidence_actions", x => x.id);
                    table.CheckConstraint("ck_accounting_evidence_action", "evidence_kind IN ('ECL','INVENTORY','SPECIALIST','ANALYTICAL','JOURNAL_RISK') AND actor_epoch >= 1 AND ((action='LINK' AND result_id IS NOT NULL AND link_id IS NOT NULL AND decision='') OR (action='REVIEW' AND result_id IS NULL AND link_id IS NULL AND decision IN ('APPROVED','CHANGES_REQUIRED','REJECTED','ESCALATED'))) AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND length(trim(reason)) BETWEEN 1 AND 4000 AND length(trim(evidence_reference)) BETWEEN 1 AND 2000 AND length(before_json) BETWEEN 1 AND 250000 AND length(after_json) BETWEEN 1 AND 250000");
                    table.ForeignKey(
                        name: "FK_accounting_evidence_actions_accounting_evidence_audit_links~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.link_id },
                        principalTable: "accounting_evidence_audit_links",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accounting_evidence_actions_audit_procedure_results_firm_id~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id, x.result_id },
                        principalTable: "audit_procedure_results",
                        principalColumns: new[] { "firm_id", "client_id", "engagement_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accounting_evidence_actions_engagements_firm_id_client_id_e~",
                        columns: x => new { x.firm_id, x.client_id, x.engagement_id },
                        principalTable: "engagements",
                        principalColumns: new[] { "firm_id", "practice_client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accounting_evidence_actions_users_firm_id_actor_id",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_evidence_actions_firm_id_actor_id_request_id",
                table: "accounting_evidence_actions",
                columns: new[] { "firm_id", "actor_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_evidence_actions_firm_id_client_id_engagement_i~1",
                table: "accounting_evidence_actions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "result_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_evidence_actions_firm_id_client_id_engagement_id~",
                table: "accounting_evidence_actions",
                columns: new[] { "firm_id", "client_id", "engagement_id", "link_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_evidence_actions_firm_id_evidence_kind_evidence_~",
                table: "accounting_evidence_actions",
                columns: new[] { "firm_id", "evidence_kind", "evidence_id", "created_at" });
            migrationBuilder.Sql("""
                CREATE TRIGGER accounting_evidence_actions_append_only
                  BEFORE UPDATE OR DELETE ON accounting_evidence_actions
                  FOR EACH ROW EXECUTE FUNCTION prevent_completion_evidence_mutation();
                CREATE FUNCTION enforce_native_accounting_evidence_action() RETURNS trigger AS $$
                DECLARE target_table text; target jsonb; linked accounting_evidence_audit_links%ROWTYPE;
                BEGIN
                  target_table := CASE NEW.evidence_kind
                    WHEN 'ECL' THEN 'ecl_assessments' WHEN 'INVENTORY' THEN 'inventory_valuation_assessments'
                    WHEN 'SPECIALIST' THEN 'specialist_accounting_schedules' WHEN 'ANALYTICAL' THEN 'analytical_reviews'
                    WHEN 'JOURNAL_RISK' THEN 'journal_risk_flags' END;
                  EXECUTE format('SELECT to_jsonb(t) FROM %I t WHERE id=$1 AND firm_id=$2 AND client_id=$3 AND engagement_id=$4 FOR SHARE',target_table)
                    INTO target USING NEW.evidence_id,NEW.firm_id,NEW.client_id,NEW.engagement_id;
                  IF target IS NULL OR (NEW.before_json::jsonb->>'Id')::uuid IS DISTINCT FROM NEW.evidence_id
                    OR (NEW.after_json::jsonb->>'Id')::uuid IS DISTINCT FROM NEW.evidence_id
                    OR NEW.before_json::jsonb->>'Kind' IS DISTINCT FROM NEW.evidence_kind
                    OR NEW.after_json::jsonb->>'Kind' IS DISTINCT FROM NEW.evidence_kind
                    OR (NEW.after_json::jsonb->>'ClientId')::uuid IS DISTINCT FROM NEW.client_id
                    OR (NEW.after_json::jsonb->>'EngagementId')::uuid IS DISTINCT FROM NEW.engagement_id THEN
                    RAISE EXCEPTION 'Native accounting evidence action requires exact scoped snapshots';
                  END IF;
                  IF NEW.action='REVIEW' THEN
                    IF target->>'status' IS DISTINCT FROM NEW.decision
                      OR (target->>'reviewed_by_user_id')::uuid IS DISTINCT FROM NEW.actor_id
                      OR target->>'reviewed_at' IS NULL
                      OR (target->>'created_by_user_id')::uuid IS NOT DISTINCT FROM NEW.actor_id
                      OR EXISTS(SELECT 1 FROM accounting_evidence_actions WHERE firm_id=NEW.firm_id
                        AND evidence_kind=NEW.evidence_kind AND evidence_id=NEW.evidence_id AND action='REVIEW') THEN
                      RAISE EXCEPTION 'Native review requires one independent retained decision';
                    END IF;
                  ELSE
                    SELECT * INTO linked FROM accounting_evidence_audit_links WHERE id=NEW.link_id;
                    IF linked.evidence_kind IS DISTINCT FROM NEW.evidence_kind OR linked.evidence_id IS DISTINCT FROM NEW.evidence_id
                      OR linked.audit_procedure_result_id IS DISTINCT FROM NEW.result_id OR linked.linked_by_user_id IS DISTINCT FROM NEW.actor_id THEN
                      RAISE EXCEPTION 'Native link receipt must bind the exact retained link';
                    END IF;
                  END IF;
                  RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER accounting_evidence_actions_scope_guard BEFORE INSERT ON accounting_evidence_actions
                  FOR EACH ROW EXECUTE FUNCTION enforce_native_accounting_evidence_action();
                CREATE FUNCTION protect_native_accounting_evidence_link() RETURNS trigger AS $$
                BEGIN
                  IF TG_OP IN ('UPDATE','DELETE') AND EXISTS(SELECT 1 FROM accounting_evidence_actions WHERE link_id=OLD.id) THEN
                    RAISE EXCEPTION 'Native accounting evidence link is append-only';
                  END IF;
                  IF TG_OP='INSERT' AND EXISTS(SELECT 1 FROM accounting_evidence_actions WHERE firm_id=NEW.firm_id
                    AND evidence_kind=NEW.evidence_kind AND evidence_id=NEW.evidence_id AND action='REVIEW') THEN
                    RAISE EXCEPTION 'Native reviewed evidence links are frozen';
                  END IF;
                  IF TG_OP='DELETE' THEN RETURN OLD; END IF;
                  RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER native_accounting_evidence_link_frozen BEFORE INSERT OR UPDATE OR DELETE ON accounting_evidence_audit_links
                  FOR EACH ROW EXECUTE FUNCTION protect_native_accounting_evidence_link();
                CREATE FUNCTION protect_native_accounting_evidence_review() RETURNS trigger AS $$
                DECLARE kind text;
                BEGIN
                  kind := CASE TG_TABLE_NAME WHEN 'ecl_assessments' THEN 'ECL' WHEN 'inventory_valuation_assessments' THEN 'INVENTORY'
                    WHEN 'specialist_accounting_schedules' THEN 'SPECIALIST' WHEN 'analytical_reviews' THEN 'ANALYTICAL'
                    WHEN 'journal_risk_flags' THEN 'JOURNAL_RISK' END;
                  IF EXISTS(SELECT 1 FROM accounting_evidence_actions WHERE firm_id=OLD.firm_id AND evidence_kind=kind AND evidence_id=OLD.id AND action='REVIEW') THEN
                    RAISE EXCEPTION 'Native reviewed evidence is immutable; prepare a new revision';
                  END IF;
                  IF TG_OP='DELETE' THEN RETURN OLD; END IF;
                  RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER native_ecl_review_frozen BEFORE UPDATE OR DELETE ON ecl_assessments FOR EACH ROW EXECUTE FUNCTION protect_native_accounting_evidence_review();
                CREATE TRIGGER native_inventory_review_frozen BEFORE UPDATE OR DELETE ON inventory_valuation_assessments FOR EACH ROW EXECUTE FUNCTION protect_native_accounting_evidence_review();
                CREATE TRIGGER native_specialist_review_frozen BEFORE UPDATE OR DELETE ON specialist_accounting_schedules FOR EACH ROW EXECUTE FUNCTION protect_native_accounting_evidence_review();
                CREATE TRIGGER native_analytical_review_frozen BEFORE UPDATE OR DELETE ON analytical_reviews FOR EACH ROW EXECUTE FUNCTION protect_native_accounting_evidence_review();
                CREATE TRIGGER native_risk_review_frozen BEFORE UPDATE OR DELETE ON journal_risk_flags FOR EACH ROW EXECUTE FUNCTION protect_native_accounting_evidence_review();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS(SELECT 1 FROM accounting_evidence_actions) THEN
                    RAISE EXCEPTION 'Retained native accounting evidence prevents migration rollback';
                  END IF;
                END $$;
                DROP TRIGGER native_ecl_review_frozen ON ecl_assessments;
                DROP TRIGGER native_inventory_review_frozen ON inventory_valuation_assessments;
                DROP TRIGGER native_specialist_review_frozen ON specialist_accounting_schedules;
                DROP TRIGGER native_analytical_review_frozen ON analytical_reviews;
                DROP TRIGGER native_risk_review_frozen ON journal_risk_flags;
                DROP TRIGGER native_accounting_evidence_link_frozen ON accounting_evidence_audit_links;
                DROP FUNCTION protect_native_accounting_evidence_link();
                DROP FUNCTION protect_native_accounting_evidence_review();
                DROP FUNCTION enforce_native_accounting_evidence_action() CASCADE;
                """);
            migrationBuilder.DropTable(
                name: "accounting_evidence_actions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_accounting_evidence_audit_links_firm_id_client_id_engagemen~",
                table: "accounting_evidence_audit_links");
        }
    }
}
