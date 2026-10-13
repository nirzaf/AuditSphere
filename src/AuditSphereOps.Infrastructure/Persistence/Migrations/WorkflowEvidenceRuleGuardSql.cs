namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

internal static class WorkflowEvidenceRuleGuardSql
{
  internal const string Up = """
    CREATE FUNCTION prevent_workflow_evidence_rule_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN RAISE EXCEPTION 'Workflow evidence rule history is append-only'; END; $$;
    CREATE TRIGGER prevent_workflow_evidence_rule_mutation BEFORE UPDATE OR DELETE ON workflow_task_evidence_rules
      FOR EACH ROW EXECUTE FUNCTION prevent_workflow_evidence_rule_mutation();

    CREATE FUNCTION guard_workflow_evidence_rule_revision() RETURNS trigger LANGUAGE plpgsql AS $$
    DECLARE binding workflow_task_bindings; prior workflow_task_evidence_rules; task_state text; rule_count integer;
    BEGIN
      SELECT * INTO binding FROM workflow_task_bindings
        WHERE firm_id = NEW.firm_id AND client_id = NEW.client_id AND engagement_id = NEW.engagement_id
          AND id = NEW.workflow_task_binding_id FOR UPDATE;
      IF NOT FOUND THEN RAISE EXCEPTION 'Workflow evidence rule scope is invalid'; END IF;
      SELECT status INTO task_state FROM work_tasks WHERE firm_id = NEW.firm_id AND id = binding.work_task_id;
      IF task_state IN ('COMPLETED', 'CANCELLED') THEN RAISE EXCEPTION 'Closed task prerequisites are protected'; END IF;
      SELECT * INTO prior FROM workflow_task_evidence_rules WHERE firm_id = NEW.firm_id
        AND workflow_task_binding_id = NEW.workflow_task_binding_id AND rule_key = NEW.rule_key
        ORDER BY version DESC LIMIT 1;
      IF NOT FOUND THEN
        SELECT count(DISTINCT rule_key) INTO rule_count FROM workflow_task_evidence_rules
          WHERE firm_id = NEW.firm_id AND workflow_task_binding_id = NEW.workflow_task_binding_id;
        IF NEW.version <> 1 OR NEW.state <> 'REQUIRED' OR rule_count >= 8
          THEN RAISE EXCEPTION 'Invalid first evidence rule revision or rule limit'; END IF;
      ELSE
        IF NEW.version <> prior.version + 1 OR NEW.state = prior.state OR NEW.kind <> prior.kind
          OR NEW.source_id <> prior.source_id OR NEW.pbc_request_id IS DISTINCT FROM prior.pbc_request_id
          OR NEW.audit_procedure_id IS DISTINCT FROM prior.audit_procedure_id OR NEW.task_revision <= prior.task_revision
          THEN RAISE EXCEPTION 'Evidence rule revisions must retain the exact reference and advance'; END IF;
      END IF;
      RETURN NEW;
    END; $$;
    CREATE TRIGGER guard_workflow_evidence_rule_revision BEFORE INSERT ON workflow_task_evidence_rules
      FOR EACH ROW EXECUTE FUNCTION guard_workflow_evidence_rule_revision();

    CREATE FUNCTION guard_workflow_evidence_task_revision() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN
      IF NOT EXISTS (SELECT 1 FROM workflow_task_bindings WHERE firm_id = NEW.firm_id
        AND id = NEW.workflow_task_binding_id AND revision >= NEW.task_revision)
        THEN RAISE EXCEPTION 'The task revision must include its evidence rule change'; END IF;
      RETURN NEW;
    END; $$;
    CREATE CONSTRAINT TRIGGER guard_workflow_evidence_task_revision AFTER INSERT ON workflow_task_evidence_rules
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION guard_workflow_evidence_task_revision();
    """;

  internal const string Down = """
    DO $$ BEGIN
      IF EXISTS (SELECT 1 FROM workflow_task_evidence_rules)
        THEN RAISE EXCEPTION 'Retained workflow evidence decisions cannot be downgraded'; END IF;
    END; $$;
    DROP TRIGGER IF EXISTS guard_workflow_evidence_task_revision ON workflow_task_evidence_rules;
    DROP FUNCTION IF EXISTS guard_workflow_evidence_task_revision();
    DROP TRIGGER IF EXISTS guard_workflow_evidence_rule_revision ON workflow_task_evidence_rules;
    DROP FUNCTION IF EXISTS guard_workflow_evidence_rule_revision();
    DROP TRIGGER IF EXISTS prevent_workflow_evidence_rule_mutation ON workflow_task_evidence_rules;
    DROP FUNCTION IF EXISTS prevent_workflow_evidence_rule_mutation();
    """;
}
