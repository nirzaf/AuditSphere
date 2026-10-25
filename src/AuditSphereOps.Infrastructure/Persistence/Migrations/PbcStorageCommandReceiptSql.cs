namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

// Install after pbc_storage_command_receipts. A receipt must, at commit, point at the real effect of its action on the
// same upload: the storage event of that kind written by the same actor, or the backfilled registration. Receipts are
// append-only, and Down is refused while any exists.
internal static class PbcStorageCommandReceiptSql
{
  internal const string Install = """
    CREATE OR REPLACE FUNCTION enforce_pbc_storage_command_receipt_effect() RETURNS trigger
    LANGUAGE plpgsql AS $$
    BEGIN
      IF NEW.pbc_storage_event_id IS NOT NULL AND NOT EXISTS (
          SELECT 1 FROM pbc_storage_events e
          JOIN pbc_storage_dispositions d ON d.firm_id = e.firm_id AND d.id = e.pbc_storage_disposition_id
          WHERE e.firm_id = NEW.firm_id AND e.id = NEW.pbc_storage_event_id AND
            d.pbc_upload_intent_id = NEW.pbc_upload_intent_id AND e.actor_user_id = NEW.actor_id AND
            e.kind = CASE NEW.action WHEN 'ACTIVATE_REMOTE_READ' THEN 'REMOTE_READ_ACTIVATED'
                                     WHEN 'REVERT_TO_LOCAL_READ' THEN 'REMOTE_READ_REVERTED'
                                     WHEN 'RELEASE_LOCAL_COPY' THEN 'LOCAL_COPY_RELEASED' END) THEN
        RAISE EXCEPTION 'PBC storage command receipt does not match its storage event.' USING ERRCODE = '23514';
      END IF;
      IF NEW.pbc_document_registration_id IS NOT NULL AND NOT EXISTS (
          SELECT 1 FROM pbc_document_registrations r
          WHERE r.firm_id = NEW.firm_id AND r.id = NEW.pbc_document_registration_id AND
            r.pbc_upload_intent_id = NEW.pbc_upload_intent_id) THEN
        RAISE EXCEPTION 'PBC storage command receipt does not match its registration.' USING ERRCODE = '23514';
      END IF;
      RETURN NULL;
    END;
    $$;
    CREATE CONSTRAINT TRIGGER trg_pbc_storage_command_receipt_effect
      AFTER INSERT ON pbc_storage_command_receipts DEFERRABLE INITIALLY DEFERRED
      FOR EACH ROW EXECUTE FUNCTION enforce_pbc_storage_command_receipt_effect();

    CREATE OR REPLACE FUNCTION prevent_pbc_storage_command_receipt_mutation() RETURNS trigger
    LANGUAGE plpgsql AS $$
    BEGIN
      RAISE EXCEPTION 'PBC storage command receipts are append-only evidence.' USING ERRCODE = '55000';
    END;
    $$;
    CREATE TRIGGER trg_pbc_storage_command_receipt_append_only
      BEFORE UPDATE OR DELETE ON pbc_storage_command_receipts
      FOR EACH ROW EXECUTE FUNCTION prevent_pbc_storage_command_receipt_mutation();
    CREATE TRIGGER trg_pbc_storage_command_receipt_no_truncate
      BEFORE TRUNCATE ON pbc_storage_command_receipts
      FOR EACH STATEMENT EXECUTE FUNCTION prevent_pbc_storage_command_receipt_mutation();
    """;

  internal const string Uninstall = """
    DO $$
    BEGIN
      IF EXISTS (SELECT 1 FROM pbc_storage_command_receipts) THEN
        RAISE EXCEPTION 'Cannot remove PBC storage command receipt evidence.' USING ERRCODE = '55000';
      END IF;
    END;
    $$;
    DROP TRIGGER IF EXISTS trg_pbc_storage_command_receipt_no_truncate ON pbc_storage_command_receipts;
    DROP TRIGGER IF EXISTS trg_pbc_storage_command_receipt_append_only ON pbc_storage_command_receipts;
    DROP FUNCTION IF EXISTS prevent_pbc_storage_command_receipt_mutation();
    DROP TRIGGER IF EXISTS trg_pbc_storage_command_receipt_effect ON pbc_storage_command_receipts;
    DROP FUNCTION IF EXISTS enforce_pbc_storage_command_receipt_effect();
    """;
}
