using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NativeAssessmentCommandReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assessment_command_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_epoch = table.Column<long>(type: "bigint", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    review_basis = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    preview_json = table.Column<string>(type: "text", nullable: false),
                    generation = table.Column<long>(type: "bigint", nullable: false),
                    result_generation = table.Column<long>(type: "bigint", nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assessment_command_receipts", x => x.id);
                    table.CheckConstraint("ck_assessment_receipt", "actor_epoch >= 1 AND generation >= 1 AND result_generation >= generation AND kind IN ('ANSWER','REQUEST_REVIEW','RECORD_REVIEW','DECISION','CONTINUANCE') AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND resource_id <> '00000000-0000-0000-0000-000000000000'::uuid AND request_hash ~ '^[0-9a-f]{64}$' AND review_basis ~ '^[0-9a-f]{64}$' AND length(preview_json) BETWEEN 1 AND 100000");
                    table.ForeignKey(
                        name: "FK_assessment_command_receipts_practice_clients_firm_id_client~",
                        columns: x => new { x.firm_id, x.client_id },
                        principalTable: "practice_clients",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_assessment_command_receipts_users_firm_id_actor_id",
                        columns: x => new { x.firm_id, x.actor_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_assessment_command_receipts_firm_id_actor_id_request_id",
                table: "assessment_command_receipts",
                columns: new[] { "firm_id", "actor_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_assessment_command_receipts_firm_id_client_id",
                table: "assessment_command_receipts",
                columns: new[] { "firm_id", "client_id" });
            migrationBuilder.Sql("""
                CREATE TRIGGER assessment_command_receipt_immutable BEFORE UPDATE OR DELETE ON assessment_command_receipts
                  FOR EACH ROW EXECUTE FUNCTION prevent_completion_evidence_mutation();
                CREATE FUNCTION enforce_assessment_command_receipt() RETURNS trigger AS $$
                DECLARE p jsonb := NEW.preview_json::jsonb; f jsonb := p->'Fields';
                BEGIN
                  IF NOT EXISTS (
                    SELECT 1 FROM users u JOIN client_safety_states s ON s.firm_id=u.firm_id AND s.id=NEW.client_id
                    WHERE u.id=NEW.actor_id AND u.firm_id=NEW.firm_id AND NOT u.disabled
                      AND lower(u.user_kind)<>'client' AND u.session_epoch=NEW.actor_epoch
                      AND s.input_generation=NEW.result_generation
                      AND (p->>'ClientId')::uuid=NEW.client_id AND (p->>'RequestId')::uuid=NEW.request_id
                      AND p->>'RequestHash'=NEW.request_hash AND p->>'ReviewBasis'=NEW.review_basis
                      AND f->>'Kind'=NEW.kind AND (f->>'Generation')::bigint=NEW.generation
                      AND (p->'Before'->>'Generation')::bigint=NEW.generation
                      AND ((NEW.kind='CONTINUANCE' AND NEW.result_generation::numeric=NEW.generation::numeric+1)
                        OR (NEW.kind<>'CONTINUANCE' AND NEW.result_generation=NEW.generation))
                      AND EXISTS(SELECT 1 FROM role_grants r WHERE r.firm_id=NEW.firm_id AND r.user_id=NEW.actor_id
                        AND r.engagement_id IS NULL AND (r.client_id IS NULL OR r.client_id=NEW.client_id)
                        AND r.revoked_at IS NULL AND (r.expires_at IS NULL OR r.expires_at>statement_timestamp())
                        AND ((NEW.kind='DECISION' AND lower(r.role)='partner')
                          OR (NEW.kind IN ('CONTINUANCE','RECORD_REVIEW') AND lower(r.role) IN ('partner','manager'))
                          OR (NEW.kind IN ('ANSWER','REQUEST_REVIEW') AND lower(r.role) IN ('partner','manager','senior','staff','engagementleader','auditor'))))
                  ) THEN RAISE EXCEPTION 'Assessment receipt requires current scoped authority and exact review' USING ERRCODE='23514'; END IF;
                  IF NOT (
                    (NEW.kind='ANSWER' AND EXISTS(SELECT 1 FROM evaluation_responses a
                      WHERE a.id=NEW.resource_id AND a.firm_id=NEW.firm_id AND a.practice_client_id=NEW.client_id
                        AND a.generation=NEW.generation AND a.question_id=f->>'QuestionCode'
                        AND (lower(a.answer)=lower(f->>'Answer') OR
                          EXISTS(SELECT 1 FROM question_definitions q JOIN questionnaire_templates t ON t.id=q.template_id
                            WHERE t.is_active AND t.bank=a.bank AND q.question_code=a.question_id AND q.answer_type='BOOLEAN'
                              AND lower(a.answer)=CASE lower(f->>'Answer') WHEN 'y' THEN 'yes' WHEN 'true' THEN 'yes'
                                WHEN 'n' THEN 'no' WHEN 'false' THEN 'no' ELSE lower(f->>'Answer') END))
                        AND a.evidence_reference IS NOT DISTINCT FROM (f->>'Evidence')))
                    OR (NEW.kind='REQUEST_REVIEW' AND EXISTS(SELECT 1 FROM specialist_clearances c
                      WHERE c.id=NEW.resource_id AND c.firm_id=NEW.firm_id AND c.practice_client_id=NEW.client_id
                        AND c.engagement_id IS NULL AND c.area=f->>'Area' AND c.status<>'CLEARED'))
                    OR (NEW.kind='RECORD_REVIEW' AND EXISTS(SELECT 1 FROM specialist_clearances c
                      WHERE c.id=NEW.resource_id AND c.firm_id=NEW.firm_id AND c.practice_client_id=NEW.client_id
                        AND c.engagement_id IS NULL AND (f->>'ReviewId')::uuid=c.id AND c.status=f->>'Status'
                        AND c.specialist_user_id=NEW.actor_id AND c.evidence_reference IS NOT DISTINCT FROM (f->>'Evidence')
                        AND c.conditions IS NOT DISTINCT FROM (f->>'Conditions')))
                    OR (NEW.kind='DECISION' AND EXISTS(SELECT 1 FROM acceptance_decisions d
                      WHERE d.id=NEW.resource_id AND d.firm_id=NEW.firm_id AND d.practice_client_id=NEW.client_id
                        AND d.engagement_id IS NULL AND d.generation=NEW.generation AND d.decided_by_user_id=NEW.actor_id
                        AND d.service_route=f->>'ServiceRoute' AND d.decision=f->>'Decision' AND d.rationale=f->>'Rationale'
                        AND d.conditions IS NOT DISTINCT FROM (f->>'Conditions')))
                    OR (NEW.kind='CONTINUANCE' AND EXISTS(SELECT 1 FROM acceptance_decisions d
                      WHERE d.id=NEW.resource_id AND d.firm_id=NEW.firm_id AND d.practice_client_id=NEW.client_id
                        AND d.engagement_id IS NULL AND d.generation=NEW.result_generation AND d.decision='Pending'
                        AND d.path='CONTINUANCE' AND EXISTS(SELECT 1 FROM acceptance_decisions prior
                          WHERE prior.id=d.prior_decision_id AND prior.firm_id=NEW.firm_id AND prior.practice_client_id=NEW.client_id
                            AND prior.generation=NEW.generation AND prior.decision IN ('Accepted','AcceptedWithConditions'))))
                  ) THEN RAISE EXCEPTION 'Assessment receipt requires the exact committed result' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE CONSTRAINT TRIGGER assessment_command_receipt_publication AFTER INSERT ON assessment_command_receipts
                  DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION enforce_assessment_command_receipt();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS(SELECT 1 FROM assessment_command_receipts) THEN
                    RAISE EXCEPTION 'Retained assessment command evidence prevents rollback';
                  END IF;
                END $$;
                DROP FUNCTION enforce_assessment_command_receipt() CASCADE;
                """);
            migrationBuilder.DropTable(
                name: "assessment_command_receipts");
        }
    }
}
