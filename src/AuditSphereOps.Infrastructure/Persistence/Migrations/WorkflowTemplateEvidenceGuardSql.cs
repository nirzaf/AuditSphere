namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

internal static class WorkflowTemplateEvidenceGuardSql
{
  internal const string Up = """
    CREATE FUNCTION guard_workflow_template_evidence_declaration() RETURNS trigger LANGUAGE plpgsql AS $$
    DECLARE revision_state text; declaration_count integer;
    BEGIN
      IF TG_OP = 'UPDATE' AND ROW(NEW.id, NEW.firm_id, NEW.template_revision_id, NEW.template_step_id)
          IS DISTINCT FROM ROW(OLD.id, OLD.firm_id, OLD.template_revision_id, OLD.template_step_id) THEN
        RAISE EXCEPTION 'Template evidence declaration identity is immutable';
      END IF;
      IF TG_OP <> 'INSERT' THEN
        SELECT status INTO revision_state FROM workflow_template_revisions
          WHERE firm_id = OLD.firm_id AND id = OLD.template_revision_id FOR UPDATE;
        IF revision_state IS DISTINCT FROM 'DRAFT' THEN
          RAISE EXCEPTION 'Reviewed template evidence declarations are immutable';
        END IF;
      END IF;
      IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
      SELECT status INTO revision_state FROM workflow_template_revisions
        WHERE firm_id = NEW.firm_id AND id = NEW.template_revision_id FOR UPDATE;
      IF revision_state IS DISTINCT FROM 'DRAFT' THEN
        RAISE EXCEPTION 'Evidence declarations require a draft template revision';
      END IF;
      SELECT count(*) INTO declaration_count FROM workflow_template_evidence_requirements
        WHERE firm_id = NEW.firm_id AND template_revision_id = NEW.template_revision_id
          AND template_step_id = NEW.template_step_id AND id <> NEW.id;
      IF declaration_count >= 8 THEN RAISE EXCEPTION 'Template evidence declaration limit exceeded'; END IF;
      RETURN NEW;
    END; $$;
    CREATE TRIGGER guard_workflow_template_evidence_declaration
      BEFORE INSERT OR UPDATE OR DELETE ON workflow_template_evidence_requirements
      FOR EACH ROW EXECUTE FUNCTION guard_workflow_template_evidence_declaration();

    CREATE FUNCTION guard_workflow_evidence_template_origin() RETURNS trigger LANGUAGE plpgsql AS $$
    DECLARE prior_origin uuid;
    BEGIN
      SELECT workflow_template_evidence_requirement_id INTO prior_origin FROM workflow_task_evidence_rules
        WHERE firm_id = NEW.firm_id AND workflow_task_binding_id = NEW.workflow_task_binding_id
          AND rule_key = NEW.rule_key ORDER BY version DESC LIMIT 1;
      IF FOUND AND NEW.workflow_template_evidence_requirement_id IS DISTINCT FROM prior_origin THEN
        RAISE EXCEPTION 'Evidence rule template origin is retained across revisions';
      END IF;
      IF NEW.workflow_template_evidence_requirement_id IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM workflow_template_evidence_requirements requirement
        JOIN workflow_task_bindings binding ON binding.firm_id = requirement.firm_id
          AND binding.template_revision_id = requirement.template_revision_id
          AND binding.template_step_id = requirement.template_step_id
        WHERE binding.firm_id = NEW.firm_id AND binding.client_id = NEW.client_id
          AND binding.engagement_id = NEW.engagement_id AND binding.id = NEW.workflow_task_binding_id
          AND requirement.id = NEW.workflow_template_evidence_requirement_id AND requirement.kind = NEW.kind
      ) THEN RAISE EXCEPTION 'Evidence rule template origin does not match its task'; END IF;
      RETURN NEW;
    END; $$;
    CREATE TRIGGER z_guard_workflow_evidence_template_origin BEFORE INSERT ON workflow_task_evidence_rules
      FOR EACH ROW EXECUTE FUNCTION guard_workflow_evidence_template_origin();
    """;

  internal const string Down = """
    DROP TRIGGER IF EXISTS z_guard_workflow_evidence_template_origin ON workflow_task_evidence_rules;
    DROP FUNCTION IF EXISTS guard_workflow_evidence_template_origin();
    DROP TRIGGER IF EXISTS guard_workflow_template_evidence_declaration ON workflow_template_evidence_requirements;
    DROP FUNCTION IF EXISTS guard_workflow_template_evidence_declaration();
    """;
}
