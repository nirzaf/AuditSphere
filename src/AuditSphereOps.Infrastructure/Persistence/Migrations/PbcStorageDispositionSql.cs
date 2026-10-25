namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

// Install after pbc_storage_dispositions/pbc_storage_events. A disposition only moves through the reviewed
// storage transitions (activate, pre-cleanup revert, release), one revision at a time, and every revision has
// exactly one matching append-only event committed with it. Released local copies are terminal.
internal static class PbcStorageDispositionSql
{
  internal const string Install = """
    CREATE OR REPLACE FUNCTION guard_pbc_storage_disposition_change() RETURNS trigger
    LANGUAGE plpgsql AS $$
    BEGIN
      IF TG_OP <> 'UPDATE' THEN
        RAISE EXCEPTION 'PBC storage dispositions cannot be removed.' USING ERRCODE = '55000';
      END IF;
      IF NEW.id <> OLD.id OR NEW.firm_id <> OLD.firm_id OR NEW.client_id <> OLD.client_id OR
         NEW.engagement_id <> OLD.engagement_id OR NEW.pbc_upload_intent_id <> OLD.pbc_upload_intent_id OR
         NEW.pbc_document_registration_id <> OLD.pbc_document_registration_id OR NEW.revision <> OLD.revision + 1 OR
         NOT ((OLD.read_source = 'LOCAL_STAGED' AND OLD.local_copy = 'RETAINED' AND
               NEW.read_source = 'SELECTED_SITE' AND NEW.local_copy = 'RETAINED') OR
              (OLD.read_source = 'SELECTED_SITE' AND OLD.local_copy = 'RETAINED' AND
               NEW.read_source = 'LOCAL_STAGED' AND NEW.local_copy = 'RETAINED') OR
              (OLD.read_source = 'SELECTED_SITE' AND OLD.local_copy = 'RETAINED' AND
               NEW.read_source = 'SELECTED_SITE' AND NEW.local_copy = 'RELEASED')) THEN
        RAISE EXCEPTION 'Unsupported PBC storage disposition transition.' USING ERRCODE = '55000';
      END IF;
      RETURN NEW;
    END;
    $$;
    CREATE TRIGGER trg_pbc_storage_disposition_guard
      BEFORE UPDATE OR DELETE ON pbc_storage_dispositions
      FOR EACH ROW EXECUTE FUNCTION guard_pbc_storage_disposition_change();
    CREATE TRIGGER trg_pbc_storage_disposition_no_truncate
      BEFORE TRUNCATE ON pbc_storage_dispositions
      FOR EACH STATEMENT EXECUTE FUNCTION guard_pbc_storage_disposition_change();

    CREATE OR REPLACE FUNCTION enforce_pbc_storage_disposition_evidence() RETURNS trigger
    LANGUAGE plpgsql AS $$
    DECLARE current_row pbc_storage_dispositions%ROWTYPE;
    BEGIN
      SELECT * INTO current_row FROM pbc_storage_dispositions WHERE firm_id = NEW.firm_id AND id = NEW.id;
      IF NOT FOUND THEN RETURN NULL; END IF;
      IF (TG_OP = 'INSERT' AND (current_row.revision < 1 OR NOT EXISTS (
            SELECT 1 FROM pbc_storage_events e WHERE e.firm_id = NEW.firm_id AND e.pbc_storage_disposition_id = NEW.id AND
              e.revision = 1 AND e.kind = 'REMOTE_READ_ACTIVATED'))) OR
         NOT EXISTS (SELECT 1 FROM pbc_document_registrations r WHERE r.firm_id = current_row.firm_id AND
           r.client_id = current_row.client_id AND r.engagement_id = current_row.engagement_id AND
           r.id = current_row.pbc_document_registration_id AND r.pbc_upload_intent_id = current_row.pbc_upload_intent_id) OR
         NOT EXISTS (SELECT 1 FROM pbc_storage_events e WHERE e.firm_id = current_row.firm_id AND
           e.pbc_storage_disposition_id = current_row.id AND e.revision = current_row.revision AND
           e.read_source = current_row.read_source AND e.local_copy = current_row.local_copy) OR
         EXISTS (SELECT 1 FROM pbc_storage_events e WHERE e.firm_id = current_row.firm_id AND
           e.pbc_storage_disposition_id = current_row.id AND e.revision > current_row.revision) THEN
        RAISE EXCEPTION 'PBC storage disposition revisions require exactly matching transition evidence.' USING ERRCODE = '23514';
      END IF;
      RETURN NULL;
    END;
    $$;
    CREATE CONSTRAINT TRIGGER trg_pbc_storage_disposition_evidence
      AFTER INSERT OR UPDATE ON pbc_storage_dispositions DEFERRABLE INITIALLY DEFERRED
      FOR EACH ROW EXECUTE FUNCTION enforce_pbc_storage_disposition_evidence();

    CREATE OR REPLACE FUNCTION prevent_pbc_storage_event_mutation() RETURNS trigger
    LANGUAGE plpgsql AS $$
    BEGIN
      RAISE EXCEPTION 'PBC storage events are append-only evidence.' USING ERRCODE = '55000';
    END;
    $$;
    CREATE TRIGGER trg_pbc_storage_event_append_only
      BEFORE UPDATE OR DELETE ON pbc_storage_events
      FOR EACH ROW EXECUTE FUNCTION prevent_pbc_storage_event_mutation();
    CREATE TRIGGER trg_pbc_storage_event_no_truncate
      BEFORE TRUNCATE ON pbc_storage_events
      FOR EACH STATEMENT EXECUTE FUNCTION prevent_pbc_storage_event_mutation();
    """;

  internal const string Uninstall = """
    DO $$
    BEGIN
      IF EXISTS (SELECT 1 FROM pbc_storage_events) THEN
        RAISE EXCEPTION 'Cannot remove PBC storage disposition evidence.' USING ERRCODE = '55000';
      END IF;
    END;
    $$;
    DROP TRIGGER IF EXISTS trg_pbc_storage_event_no_truncate ON pbc_storage_events;
    DROP TRIGGER IF EXISTS trg_pbc_storage_event_append_only ON pbc_storage_events;
    DROP FUNCTION IF EXISTS prevent_pbc_storage_event_mutation();
    DROP TRIGGER IF EXISTS trg_pbc_storage_disposition_evidence ON pbc_storage_dispositions;
    DROP FUNCTION IF EXISTS enforce_pbc_storage_disposition_evidence();
    DROP TRIGGER IF EXISTS trg_pbc_storage_disposition_no_truncate ON pbc_storage_dispositions;
    DROP TRIGGER IF EXISTS trg_pbc_storage_disposition_guard ON pbc_storage_dispositions;
    DROP FUNCTION IF EXISTS guard_pbc_storage_disposition_change();
    """;
}
