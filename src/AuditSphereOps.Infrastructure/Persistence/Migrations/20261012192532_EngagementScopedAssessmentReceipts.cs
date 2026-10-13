using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

public partial class EngagementScopedAssessmentReceipts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION enforce_assessment_command_receipt() RETURNS trigger AS $$
            DECLARE p jsonb := NEW.preview_json::jsonb; f jsonb := p->'Fields';
            BEGIN
            IF NOT EXISTS (
            SELECT 1 FROM users u JOIN client_safety_states s ON s.firm_id=u.firm_id AND s.id=NEW.client_id
            WHERE u.id=NEW.actor_id AND u.firm_id=NEW.firm_id AND NOT u.disabled
            AND lower(u.user_kind)<>'client' AND u.session_epoch=NEW.actor_epoch
            AND s.input_generation=NEW.result_generation
            AND (p->>'ClientId')::uuid=NEW.client_id AND (p->>'RequestId')::uuid=NEW.request_id
            AND p->>'RequestHash'=NEW.request_hash AND p->>'ReviewBasis'=NEW.review_basis
            AND (NEW.kind='DECISION' OR NULLIF(f->>'EngagementId','') IS NULL)
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
            AND d.engagement_id IS NOT DISTINCT FROM NULLIF(f->>'EngagementId','')::uuid
            AND (d.engagement_id IS NULL OR EXISTS(SELECT 1 FROM engagements e WHERE e.id=d.engagement_id
            AND e.firm_id=NEW.firm_id AND e.practice_client_id=NEW.client_id AND e.service_route=f->>'ServiceRoute'))
            AND d.generation=NEW.generation AND d.decided_by_user_id=NEW.actor_id
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
            """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION enforce_assessment_command_receipt() RETURNS trigger AS $$
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
            """);
}
