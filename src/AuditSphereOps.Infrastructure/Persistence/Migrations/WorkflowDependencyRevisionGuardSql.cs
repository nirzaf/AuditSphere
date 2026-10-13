namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

internal static class WorkflowDependencyRevisionGuardSql
{
  internal const string Up = """
    CREATE VIEW workflow_task_effective_dependencies AS
    WITH heads AS NOT MATERIALIZED (SELECT DISTINCT ON (firm_id, successor_binding_id, predecessor_binding_id) *
      FROM workflow_task_dependency_revisions ORDER BY firm_id, successor_binding_id, predecessor_binding_id, version DESC)
    SELECT d.firm_id, d.client_id, d.engagement_id, d.predecessor_binding_id, d.successor_binding_id
      FROM workflow_task_dependencies d WHERE NOT EXISTS (SELECT 1 FROM heads h WHERE h.firm_id = d.firm_id
        AND h.successor_binding_id = d.successor_binding_id AND h.predecessor_binding_id = d.predecessor_binding_id)
    UNION ALL
    SELECT firm_id, client_id, engagement_id, predecessor_binding_id, successor_binding_id FROM heads WHERE required;

    CREATE FUNCTION prevent_workflow_dependency_revision_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN RAISE EXCEPTION 'Workflow dependency amendments are append-only'; END; $$;
    CREATE TRIGGER prevent_workflow_dependency_revision_mutation BEFORE UPDATE OR DELETE ON workflow_task_dependency_revisions
      FOR EACH ROW EXECUTE FUNCTION prevent_workflow_dependency_revision_mutation();

    CREATE FUNCTION guard_workflow_dependency_revision() RETURNS trigger LANGUAGE plpgsql AS $$
    DECLARE prior workflow_task_dependency_revisions; binding workflow_task_bindings; task_state text; baseline boolean;
    BEGIN
      PERFORM 1 FROM firm_safety_states WHERE id = NEW.firm_id FOR UPDATE;
      PERFORM 1 FROM engagements WHERE firm_id = NEW.firm_id AND id = NEW.engagement_id FOR UPDATE;
      SELECT * INTO binding FROM workflow_task_bindings WHERE firm_id = NEW.firm_id AND client_id = NEW.client_id
        AND engagement_id = NEW.engagement_id AND id = NEW.successor_binding_id FOR UPDATE;
      IF NOT FOUND THEN RAISE EXCEPTION 'Dependency successor scope is invalid'; END IF;
      SELECT status INTO task_state FROM work_tasks WHERE firm_id = NEW.firm_id AND id = binding.work_task_id;
      IF task_state IN ('COMPLETED','CANCELLED') THEN RAISE EXCEPTION 'Closed task dependencies are protected'; END IF;
      IF NEW.required AND EXISTS (SELECT 1 FROM workflow_task_bindings p JOIN work_tasks t
        ON t.firm_id = p.firm_id AND t.id = p.work_task_id WHERE p.firm_id = NEW.firm_id
        AND p.id = NEW.predecessor_binding_id AND t.status = 'CANCELLED')
        THEN RAISE EXCEPTION 'Cancelled tasks cannot be required predecessors'; END IF;
      SELECT * INTO prior FROM workflow_task_dependency_revisions WHERE firm_id = NEW.firm_id
        AND successor_binding_id = NEW.successor_binding_id AND predecessor_binding_id = NEW.predecessor_binding_id
        ORDER BY version DESC LIMIT 1;
      IF FOUND THEN
        IF NEW.version <> prior.version + 1 OR NEW.required = prior.required OR NEW.task_revision <= prior.task_revision
          THEN RAISE EXCEPTION 'Dependency amendment must toggle and advance'; END IF;
      ELSE
        SELECT EXISTS (SELECT 1 FROM workflow_task_dependencies WHERE firm_id = NEW.firm_id
          AND predecessor_binding_id = NEW.predecessor_binding_id AND successor_binding_id = NEW.successor_binding_id) INTO baseline;
        IF NEW.version <> 1 OR NEW.required = baseline THEN RAISE EXCEPTION 'Invalid first dependency amendment'; END IF;
      END IF;
      RETURN NEW;
    END; $$;
    CREATE TRIGGER guard_workflow_dependency_revision BEFORE INSERT ON workflow_task_dependency_revisions
      FOR EACH ROW EXECUTE FUNCTION guard_workflow_dependency_revision();

    CREATE FUNCTION guard_workflow_dependency_graph() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN
      IF NOT EXISTS (SELECT 1 FROM workflow_task_bindings WHERE firm_id = NEW.firm_id AND id = NEW.successor_binding_id
        AND revision >= NEW.task_revision) OR NOT EXISTS (SELECT 1 FROM workflow_task_bindings WHERE firm_id = NEW.firm_id
        AND id = NEW.predecessor_binding_id AND revision >= NEW.predecessor_revision)
        THEN RAISE EXCEPTION 'Dependency change must bind the saved task revisions'; END IF;
      IF (SELECT count(*) FROM workflow_task_bindings WHERE firm_id = NEW.firm_id AND engagement_id = NEW.engagement_id) > 500
        OR (SELECT count(*) FROM workflow_task_effective_dependencies WHERE firm_id = NEW.firm_id AND engagement_id = NEW.engagement_id) > 4000
        THEN RAISE EXCEPTION 'Workflow dependency graph exceeds the supported bound'; END IF;
      IF NEW.required AND EXISTS (
        WITH RECURSIVE reachable(id) AS (
          SELECT NEW.successor_binding_id
          UNION
          SELECT e.successor_binding_id FROM workflow_task_effective_dependencies e JOIN reachable r ON e.predecessor_binding_id = r.id
            WHERE e.firm_id = NEW.firm_id AND e.engagement_id = NEW.engagement_id
        ) SELECT 1 FROM reachable WHERE id = NEW.predecessor_binding_id)
        THEN RAISE EXCEPTION 'Workflow dependency graph must be acyclic'; END IF;
      RETURN NEW;
    END; $$;
    CREATE CONSTRAINT TRIGGER guard_workflow_dependency_graph AFTER INSERT ON workflow_task_dependency_revisions
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION guard_workflow_dependency_graph();
    """;

  internal const string Down = """
    DO $$ BEGIN
      IF EXISTS (SELECT 1 FROM workflow_task_dependency_revisions) THEN RAISE EXCEPTION 'Retained workflow dependency amendments cannot be downgraded'; END IF;
    END; $$;
    DROP TRIGGER IF EXISTS guard_workflow_dependency_graph ON workflow_task_dependency_revisions;
    DROP FUNCTION IF EXISTS guard_workflow_dependency_graph();
    DROP TRIGGER IF EXISTS guard_workflow_dependency_revision ON workflow_task_dependency_revisions;
    DROP FUNCTION IF EXISTS guard_workflow_dependency_revision();
    DROP TRIGGER IF EXISTS prevent_workflow_dependency_revision_mutation ON workflow_task_dependency_revisions;
    DROP FUNCTION IF EXISTS prevent_workflow_dependency_revision_mutation();
    DROP VIEW IF EXISTS workflow_task_effective_dependencies;
    """;
}
