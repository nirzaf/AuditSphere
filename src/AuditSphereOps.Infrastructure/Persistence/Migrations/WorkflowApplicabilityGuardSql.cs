namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

internal static class WorkflowApplicabilityGuardSql
{
  // Storage lineage is additional protection, not a replacement for current command authorization,
  // the complete application source evaluator or separate professional applicability decisions.
  internal const string Up = """
    CREATE FUNCTION prevent_workflow_applicability_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN RAISE EXCEPTION 'Workflow applicability evidence is append-only'; END; $$;
    CREATE TRIGGER prevent_workflow_applicability_proposal_mutation
      BEFORE UPDATE OR DELETE ON workflow_task_applicability_proposals
      FOR EACH ROW EXECUTE FUNCTION prevent_workflow_applicability_mutation();
    CREATE TRIGGER prevent_workflow_applicability_proposal_truncate
      BEFORE TRUNCATE ON workflow_task_applicability_proposals
      FOR EACH STATEMENT EXECUTE FUNCTION prevent_workflow_applicability_mutation();
    CREATE TRIGGER prevent_workflow_applicability_decision_mutation
      BEFORE UPDATE OR DELETE ON workflow_task_applicability_decisions
      FOR EACH ROW EXECUTE FUNCTION prevent_workflow_applicability_mutation();
    CREATE TRIGGER prevent_workflow_applicability_decision_truncate
      BEFORE TRUNCATE ON workflow_task_applicability_decisions
      FOR EACH STATEMENT EXECUTE FUNCTION prevent_workflow_applicability_mutation();

    CREATE FUNCTION guard_workflow_applicability_proposal() RETURNS trigger LANGUAGE plpgsql AS $$
    DECLARE b workflow_task_bindings; s workflow_template_steps; e engagements;
      receipt pbc_acceptance_receipts; task_state text; previous_version bigint; basis jsonb;
    BEGIN
      SELECT * INTO b FROM workflow_task_bindings WHERE firm_id = NEW.firm_id
        AND client_id = NEW.client_id AND engagement_id = NEW.engagement_id
        AND id = NEW.workflow_task_binding_id FOR UPDATE;
      IF NOT FOUND THEN RAISE EXCEPTION 'Applicability task scope is invalid'; END IF;
      SELECT * INTO s FROM workflow_template_steps WHERE firm_id = NEW.firm_id
        AND template_revision_id = NEW.template_revision_id AND id = NEW.template_step_id;
      IF NOT FOUND OR NOT s.required_task OR nullif(trim(s.applicability_condition), '') IS NULL
        OR b.template_revision_id IS DISTINCT FROM NEW.template_revision_id
        OR b.template_step_id IS DISTINCT FROM NEW.template_step_id
        OR NEW.condition_digest <> encode(sha256(convert_to(s.applicability_condition, 'UTF8')), 'hex')
        THEN RAISE EXCEPTION 'An exact conditional template step is required'; END IF;
      SELECT * INTO e FROM engagements WHERE firm_id = NEW.firm_id AND id = NEW.engagement_id
        AND practice_client_id = NEW.client_id;
      IF NOT FOUND OR e.generation <> NEW.engagement_generation
        OR b.revision <> NEW.task_revision OR b.generation <> NEW.binding_generation
        OR b.held_at IS NOT NULL OR b.superseded_at IS NOT NULL
        THEN RAISE EXCEPTION 'Applicability task basis is stale or held'; END IF;
      SELECT status INTO task_state FROM work_tasks WHERE firm_id = NEW.firm_id
        AND client_id = NEW.client_id AND engagement_id = NEW.engagement_id AND id = b.work_task_id;
      IF NOT FOUND OR task_state = 'COMPLETED'
        THEN RAISE EXCEPTION 'An available incomplete task is required'; END IF;
      SELECT * INTO receipt FROM pbc_acceptance_receipts WHERE firm_id = NEW.firm_id
        AND client_id = NEW.client_id AND engagement_id = NEW.engagement_id AND id = NEW.pbc_acceptance_receipt_id;
      IF NOT FOUND THEN RAISE EXCEPTION 'The exact accepted source is required'; END IF;
      basis := NEW.basis_json::jsonb;
      IF (basis->>'Version') IS DISTINCT FROM 'workflow-task-applicability.v1'
        OR (basis->>'FirmId') IS DISTINCT FROM NEW.firm_id::text
        OR (basis->>'ClientId') IS DISTINCT FROM NEW.client_id::text
        OR (basis->>'EngagementId') IS DISTINCT FROM NEW.engagement_id::text
        OR (basis->>'BindingId') IS DISTINCT FROM b.id::text
        OR (basis->>'WorkTaskId') IS DISTINCT FROM b.work_task_id::text
        OR (basis->>'TemplateRevisionId') IS DISTINCT FROM NEW.template_revision_id::text
        OR (basis->>'TemplateStepId') IS DISTINCT FROM NEW.template_step_id::text
        OR (basis->>'Revision') IS DISTINCT FROM b.revision::text
        OR (basis->>'Generation') IS DISTINCT FROM b.generation::text
        OR (basis->>'EngagementGeneration') IS DISTINCT FROM e.generation::text
        OR (basis->>'ConditionDigest') IS DISTINCT FROM NEW.condition_digest
        OR (basis->>'Status') IS DISTINCT FROM task_state
        OR (basis->'AcceptedSource'->>'Id') IS DISTINCT FROM receipt.id::text
        OR (basis->'AcceptedSource'->>'SourceDigest') IS DISTINCT FROM receipt.source_digest
        OR (basis->'AcceptedSource'->>'ContentSha256Hex') IS DISTINCT FROM receipt.content_sha256_hex
        THEN RAISE EXCEPTION 'Applicability snapshot does not match its retained source pins'; END IF;
      SELECT coalesce(max(version), 0) INTO previous_version FROM workflow_task_applicability_proposals
        WHERE firm_id = NEW.firm_id AND workflow_task_binding_id = b.id;
      IF previous_version = 9223372036854775807 OR NEW.version <> previous_version + 1
        THEN RAISE EXCEPTION 'Applicability proposal versions must be sequential'; END IF;
      RETURN NEW;
    END; $$;
    CREATE TRIGGER guard_workflow_applicability_proposal BEFORE INSERT ON workflow_task_applicability_proposals
      FOR EACH ROW EXECUTE FUNCTION guard_workflow_applicability_proposal();

    CREATE FUNCTION guard_workflow_applicability_decision() RETURNS trigger LANGUAGE plpgsql AS $$
    DECLARE b workflow_task_bindings; p workflow_task_applicability_proposals;
      prior workflow_task_applicability_decisions; latest_proposal uuid;
    BEGIN
      SELECT * INTO b FROM workflow_task_bindings WHERE firm_id = NEW.firm_id
        AND client_id = NEW.client_id AND engagement_id = NEW.engagement_id
        AND id = NEW.workflow_task_binding_id FOR UPDATE;
      IF NOT FOUND THEN RAISE EXCEPTION 'Applicability decision task scope is invalid'; END IF;
      SELECT * INTO p FROM workflow_task_applicability_proposals WHERE firm_id = NEW.firm_id
        AND client_id = NEW.client_id AND engagement_id = NEW.engagement_id
        AND workflow_task_binding_id = b.id AND id = NEW.proposal_id;
      IF NOT FOUND OR NEW.proposer_user_id <> p.actor_user_id OR NEW.actor_user_id = p.actor_user_id
        OR NEW.basis_hash <> p.basis_hash
        THEN RAISE EXCEPTION 'Applicability review must be independent and bind the exact proposal'; END IF;
      SELECT * INTO prior FROM workflow_task_applicability_decisions
        WHERE firm_id = NEW.firm_id AND proposal_id = p.id ORDER BY version DESC LIMIT 1;
      IF NOT FOUND THEN
        IF NEW.version <> 1 OR NEW.previous_decision_id IS NOT NULL OR NEW.kind NOT IN ('APPROVE', 'RETURN')
          THEN RAISE EXCEPTION 'Invalid first applicability decision'; END IF;
      ELSE
        IF prior.kind <> 'APPROVE' OR NEW.kind <> 'REVOKE' OR NEW.version <> prior.version + 1
          OR NEW.previous_decision_id IS DISTINCT FROM prior.id
          THEN RAISE EXCEPTION 'Only the exact effective approval may be revoked'; END IF;
      END IF;
      IF NEW.kind = 'APPROVE' THEN
        SELECT id INTO latest_proposal FROM workflow_task_applicability_proposals
          WHERE firm_id = NEW.firm_id AND workflow_task_binding_id = b.id ORDER BY version DESC LIMIT 1;
        IF latest_proposal <> p.id OR p.task_revision <> b.revision OR p.binding_generation <> b.generation
          OR b.held_at IS NOT NULL OR b.superseded_at IS NOT NULL
          OR NOT EXISTS (SELECT 1 FROM engagements WHERE firm_id = NEW.firm_id AND id = NEW.engagement_id
            AND practice_client_id = NEW.client_id AND generation = p.engagement_generation)
          OR NOT EXISTS (SELECT 1 FROM work_tasks WHERE firm_id = NEW.firm_id AND id = b.work_task_id AND status <> 'COMPLETED')
          THEN RAISE EXCEPTION 'The latest current task proposal must be independently reviewed'; END IF;
      END IF;
      RETURN NEW;
    END; $$;
    CREATE TRIGGER guard_workflow_applicability_decision BEFORE INSERT ON workflow_task_applicability_decisions
      FOR EACH ROW EXECUTE FUNCTION guard_workflow_applicability_decision();
    """;

  internal const string Down = """
    DO $$ BEGIN
      IF EXISTS (SELECT 1 FROM workflow_task_applicability_proposals)
        OR EXISTS (SELECT 1 FROM workflow_task_applicability_decisions)
        THEN RAISE EXCEPTION 'Retained applicability evidence cannot be downgraded'; END IF;
    END; $$;
    DROP FUNCTION guard_workflow_applicability_decision() CASCADE;
    DROP FUNCTION guard_workflow_applicability_proposal() CASCADE;
    DROP FUNCTION prevent_workflow_applicability_mutation() CASCADE;
    """;
}
