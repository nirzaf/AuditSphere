namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

internal static class NativeTrialBalanceSourceIntegritySql
{
  internal const string Install = """
    CREATE FUNCTION protect_native_tb_evidence() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN
      RAISE EXCEPTION 'Native trial-balance provenance is immutable.' USING ERRCODE = '55000';
    END;
    $$;
    CREATE TRIGGER trg_native_tb_evidence_immutable BEFORE UPDATE OR DELETE
      ON native_trial_balance_sources FOR EACH ROW EXECUTE FUNCTION protect_native_tb_evidence();

    CREATE FUNCTION protect_native_tb_truncate() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN
      IF EXISTS (SELECT 1 FROM native_trial_balance_sources) THEN
        RAISE EXCEPTION 'Retained native trial-balance provenance cannot be truncated.' USING ERRCODE = '55000';
      END IF;
      RETURN NULL;
    END;
    $$;
    CREATE TRIGGER trg_native_tb_evidence_truncate BEFORE TRUNCATE ON native_trial_balance_sources
      FOR EACH STATEMENT EXECUTE FUNCTION protect_native_tb_truncate();
    CREATE TRIGGER trg_native_tb_dataset_truncate BEFORE TRUNCATE ON trial_balance_datasets
      FOR EACH STATEMENT EXECUTE FUNCTION protect_native_tb_truncate();
    CREATE TRIGGER trg_native_tb_rows_truncate BEFORE TRUNCATE ON trial_balance_rows
      FOR EACH STATEMENT EXECUTE FUNCTION protect_native_tb_truncate();

    CREATE FUNCTION protect_native_tb_header() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN
      IF OLD.source_kind = 'Native' AND OLD.import_state = 'SEALED'
        AND (to_jsonb(OLD) - ARRAY['validation_status', 'balanced', 'control_total'])
          IS DISTINCT FROM (to_jsonb(NEW) - ARRAY['validation_status', 'balanced', 'control_total']) THEN
        RAISE EXCEPTION 'A sealed native source header cannot be replaced or rebound.' USING ERRCODE = '55000';
      END IF;
      RETURN NEW;
    END;
    $$;
    CREATE TRIGGER trg_native_tb_header_immutable BEFORE UPDATE ON trial_balance_datasets
      FOR EACH ROW EXECUTE FUNCTION protect_native_tb_header();

    CREATE FUNCTION require_complete_native_tb_source() RETURNS trigger LANGUAGE plpgsql AS $$
    DECLARE
      target_id uuid;
      dataset trial_balance_datasets%ROWTYPE;
      evidence native_trial_balance_sources%ROWTYPE;
    BEGIN
      IF TG_TABLE_NAME = 'native_trial_balance_sources' THEN target_id := NEW.dataset_id;
      ELSE target_id := NEW.id;
      END IF;
      SELECT * INTO dataset FROM trial_balance_datasets WHERE id = target_id FOR UPDATE;
      SELECT * INTO evidence FROM native_trial_balance_sources WHERE dataset_id = target_id;
      IF dataset.source_kind = 'Native' THEN
        IF dataset.import_state <> 'SEALED' OR evidence.dataset_id IS NULL
          OR evidence.firm_id <> dataset.firm_id OR evidence.client_id <> dataset.client_id
          OR evidence.engagement_id <> dataset.engagement_id
          OR evidence.source_identity_hash <> dataset.sha256_hex
          OR evidence.actor_id IS DISTINCT FROM dataset.imported_by_user_id
          OR evidence.row_count <> (SELECT count(*) FROM trial_balance_rows WHERE dataset_id = target_id) THEN
          RAISE EXCEPTION 'A native source must commit sealed with exact scoped provenance and rows.' USING ERRCODE = '55000';
        END IF;
      ELSIF evidence.dataset_id IS NOT NULL THEN
        RAISE EXCEPTION 'Native provenance requires its original native dataset.' USING ERRCODE = '55000';
      END IF;
      RETURN NULL;
    END;
    $$;
    CREATE CONSTRAINT TRIGGER trg_native_tb_evidence_complete AFTER INSERT ON native_trial_balance_sources
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION require_complete_native_tb_source();
    CREATE CONSTRAINT TRIGGER trg_native_tb_dataset_complete AFTER INSERT OR UPDATE ON trial_balance_datasets
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION require_complete_native_tb_source();
    """;

  // Refuse destructive rollback once history exists; do this before dropping any table or constraint.
  internal const string Remove = """
    DO $$ BEGIN
      IF EXISTS (SELECT 1 FROM native_trial_balance_sources)
        OR EXISTS (SELECT 1 FROM trial_balance_datasets WHERE source_kind = 'Native') THEN
        RAISE EXCEPTION 'Native source history must be retained; rollback is refused.' USING ERRCODE = '55000';
      END IF;
    END $$;
    DROP TRIGGER trg_native_tb_evidence_complete ON native_trial_balance_sources;
    DROP TRIGGER trg_native_tb_dataset_complete ON trial_balance_datasets;
    DROP FUNCTION require_complete_native_tb_source();
    DROP TRIGGER trg_native_tb_header_immutable ON trial_balance_datasets;
    DROP FUNCTION protect_native_tb_header();
    DROP TRIGGER trg_native_tb_evidence_truncate ON native_trial_balance_sources;
    DROP TRIGGER trg_native_tb_dataset_truncate ON trial_balance_datasets;
    DROP TRIGGER trg_native_tb_rows_truncate ON trial_balance_rows;
    DROP FUNCTION protect_native_tb_truncate();
    DROP TRIGGER trg_native_tb_evidence_immutable ON native_trial_balance_sources;
    DROP FUNCTION protect_native_tb_evidence();
    """;
}
