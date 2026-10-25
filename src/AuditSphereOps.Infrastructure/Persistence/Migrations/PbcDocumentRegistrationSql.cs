namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

// Install after pbc_document_registrations. A registration must match, at commit, the RECEIVED upload,
// its completed durable transfer result and the exact selected-site snapshot/reference/binding in the
// same firm/client/engagement scope. Registrations and the document rows they pin are append-only.
internal static class PbcDocumentRegistrationSql
{
  internal const string Install = """
    CREATE OR REPLACE FUNCTION enforce_pbc_document_registration_source() RETURNS trigger
    LANGUAGE plpgsql AS $$
    DECLARE upload_row pbc_upload_intents%ROWTYPE;
            operation_row durable_operations%ROWTYPE;
            snapshot_row document_snapshots%ROWTYPE;
            reference_row document_references%ROWTYPE;
            binding_row repository_bindings%ROWTYPE;
    BEGIN
      SELECT * INTO upload_row FROM pbc_upload_intents
        WHERE firm_id = NEW.firm_id AND client_id = NEW.client_id AND
          engagement_id = NEW.engagement_id AND id = NEW.pbc_upload_intent_id FOR KEY SHARE;
      IF NOT FOUND OR upload_row.state <> 'RECEIVED' OR
         upload_row.transfer_operation_id IS DISTINCT FROM NEW.transfer_operation_id THEN
        RAISE EXCEPTION 'PBC document registration does not match the received upload.' USING ERRCODE = '23514';
      END IF;

      SELECT * INTO operation_row FROM durable_operations
        WHERE firm_id = NEW.firm_id AND id = NEW.transfer_operation_id FOR KEY SHARE;
      IF NOT FOUND OR operation_row.operation_kind <> 'TransferPbcDocument.v1' OR
         operation_row.status <> 'COMPLETED' OR operation_row.target_id <> NEW.pbc_upload_intent_id OR
         operation_row.client_id IS DISTINCT FROM NEW.client_id OR
         operation_row.engagement_id IS DISTINCT FROM NEW.engagement_id OR
         operation_row.result_identity IS DISTINCT FROM NEW.provider_identity OR
         operation_row.result_digest IS DISTINCT FROM upload_row.provider_receipt_digest THEN
        RAISE EXCEPTION 'PBC document registration does not match the completed durable provider transfer.' USING ERRCODE = '23514';
      END IF;

      SELECT * INTO snapshot_row FROM document_snapshots
        WHERE firm_id = NEW.firm_id AND client_id = NEW.client_id AND
          engagement_id = NEW.engagement_id AND id = NEW.document_snapshot_id FOR KEY SHARE;
      IF NOT FOUND OR snapshot_row.document_reference_id <> NEW.document_reference_id OR
         snapshot_row.sha256_hex <> upload_row.final_sha256_hex OR
         snapshot_row.sha256_hex <> upload_row.provider_receipt_digest OR
         snapshot_row.byte_count <> upload_row.declared_byte_count OR
         NEW.provider_identity <> 'graph-drive-item:' || snapshot_row.drive_id || ':' ||
           snapshot_row.item_id || ':' || snapshot_row.version_id THEN
        RAISE EXCEPTION 'PBC document registration does not match the exact verified snapshot.' USING ERRCODE = '23514';
      END IF;

      SELECT * INTO reference_row FROM document_references
        WHERE firm_id = NEW.firm_id AND client_id = NEW.client_id AND
          engagement_id = NEW.engagement_id AND id = NEW.document_reference_id FOR KEY SHARE;
      SELECT * INTO binding_row FROM repository_bindings
        WHERE firm_id = NEW.firm_id AND client_id = NEW.client_id AND
          engagement_id = NEW.engagement_id AND id = reference_row.repository_binding_id FOR KEY SHARE;
      IF NOT FOUND OR reference_row.drive_id <> snapshot_row.drive_id OR
         reference_row.item_id <> snapshot_row.item_id OR binding_row.drive_id <> snapshot_row.drive_id OR
         binding_row.classification <> 'working' OR binding_row.capability_profile <> 'selected-site' THEN
        RAISE EXCEPTION 'PBC document registration does not match its scoped repository binding.' USING ERRCODE = '23514';
      END IF;
      RETURN NEW;
    END;
    $$;

    CREATE CONSTRAINT TRIGGER trg_pbc_document_registration_source
      AFTER INSERT ON pbc_document_registrations DEFERRABLE INITIALLY DEFERRED
      FOR EACH ROW EXECUTE FUNCTION enforce_pbc_document_registration_source();

    CREATE OR REPLACE FUNCTION prevent_pbc_document_registration_mutation() RETURNS trigger
    LANGUAGE plpgsql AS $$
    BEGIN
      RAISE EXCEPTION 'PBC document registrations are immutable provider evidence.' USING ERRCODE = '55000';
    END;
    $$;
    CREATE TRIGGER trg_pbc_document_registration_append_only
      BEFORE UPDATE OR DELETE ON pbc_document_registrations
      FOR EACH ROW EXECUTE FUNCTION prevent_pbc_document_registration_mutation();
    CREATE TRIGGER trg_pbc_document_registration_no_truncate
      BEFORE TRUNCATE ON pbc_document_registrations
      FOR EACH STATEMENT EXECUTE FUNCTION prevent_pbc_document_registration_mutation();

    CREATE OR REPLACE FUNCTION prevent_registered_pbc_document_mutation() RETURNS trigger
    LANGUAGE plpgsql AS $$
    BEGIN
      IF EXISTS (SELECT 1 FROM pbc_document_registrations r WHERE r.firm_id = OLD.firm_id AND
          (CASE WHEN TG_TABLE_NAME = 'document_snapshots' THEN r.document_snapshot_id
                ELSE r.document_reference_id END) = OLD.id) THEN
        RAISE EXCEPTION 'A registered PBC document identity is immutable.' USING ERRCODE = '55000';
      END IF;
      IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
      RETURN NEW;
    END;
    $$;
    CREATE TRIGGER trg_registered_pbc_snapshot_immutable
      BEFORE UPDATE OR DELETE ON document_snapshots
      FOR EACH ROW EXECUTE FUNCTION prevent_registered_pbc_document_mutation();
    CREATE TRIGGER trg_registered_pbc_reference_immutable
      BEFORE UPDATE OR DELETE ON document_references
      FOR EACH ROW EXECUTE FUNCTION prevent_registered_pbc_document_mutation();
    """;

  internal const string Uninstall = """
    DO $$
    BEGIN
      IF EXISTS (SELECT 1 FROM pbc_document_registrations) THEN
        RAISE EXCEPTION 'Cannot remove immutable PBC document registration evidence.' USING ERRCODE = '55000';
      END IF;
    END;
    $$;
    DROP TRIGGER IF EXISTS trg_registered_pbc_reference_immutable ON document_references;
    DROP TRIGGER IF EXISTS trg_registered_pbc_snapshot_immutable ON document_snapshots;
    DROP FUNCTION IF EXISTS prevent_registered_pbc_document_mutation();
    DROP TRIGGER IF EXISTS trg_pbc_document_registration_no_truncate ON pbc_document_registrations;
    DROP TRIGGER IF EXISTS trg_pbc_document_registration_append_only ON pbc_document_registrations;
    DROP TRIGGER IF EXISTS trg_pbc_document_registration_source ON pbc_document_registrations;
    DROP FUNCTION IF EXISTS prevent_pbc_document_registration_mutation();
    DROP FUNCTION IF EXISTS enforce_pbc_document_registration_source();
    """;
}
