namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

internal static class NativeGeneralLedgerSourceIntegritySql
{
  internal const string Install = """
    CREATE FUNCTION protect_native_gl_evidence() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN
      RAISE EXCEPTION 'Generated native GL provenance is immutable.' USING ERRCODE = '55000';
    END;
    $$;
    CREATE TRIGGER trg_native_gl_evidence_immutable BEFORE UPDATE OR DELETE ON native_general_ledger_sources
      FOR EACH ROW EXECUTE FUNCTION protect_native_gl_evidence();

    CREATE FUNCTION protect_native_gl_truncate() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN
      IF EXISTS (SELECT 1 FROM native_general_ledger_sources)
        OR EXISTS (SELECT 1 FROM source_import_batches WHERE origin_kind = 'NATIVE_LEDGER') THEN
        RAISE EXCEPTION 'Retained native GL history cannot be truncated.' USING ERRCODE = '55000';
      END IF;
      RETURN NULL;
    END;
    $$;
    CREATE TRIGGER trg_native_gl_evidence_truncate BEFORE TRUNCATE ON native_general_ledger_sources
      FOR EACH STATEMENT EXECUTE FUNCTION protect_native_gl_truncate();
    CREATE TRIGGER trg_native_gl_batches_truncate BEFORE TRUNCATE ON source_import_batches
      FOR EACH STATEMENT EXECUTE FUNCTION protect_native_gl_truncate();
    CREATE TRIGGER trg_native_gl_transactions_truncate BEFORE TRUNCATE ON general_ledger_transactions
      FOR EACH STATEMENT EXECUTE FUNCTION protect_native_gl_truncate();
    CREATE TRIGGER trg_native_gl_lines_truncate BEFORE TRUNCATE ON general_ledger_lines
      FOR EACH STATEMENT EXECUTE FUNCTION protect_native_gl_truncate();

    CREATE FUNCTION protect_native_gl_header() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN
      IF OLD.status = 'SEALED' AND (OLD.origin_kind = 'NATIVE_LEDGER' OR NEW.origin_kind = 'NATIVE_LEDGER')
        AND to_jsonb(OLD) IS DISTINCT FROM to_jsonb(NEW) THEN
        RAISE EXCEPTION 'A sealed source cannot be relabelled or rebound as native GL.' USING ERRCODE = '55000';
      END IF;
      IF OLD.origin_kind IS DISTINCT FROM NEW.origin_kind THEN
        RAISE EXCEPTION 'Source origin cannot be reclassified.' USING ERRCODE = '55000';
      END IF;
      RETURN NEW;
    END;
    $$;
    CREATE TRIGGER trg_native_gl_header_immutable BEFORE UPDATE ON source_import_batches
      FOR EACH ROW EXECUTE FUNCTION protect_native_gl_header();

    CREATE FUNCTION protect_native_gl_chunks() RETURNS trigger LANGUAGE plpgsql AS $$
    DECLARE origin text;
    BEGIN
      SELECT origin_kind INTO origin FROM source_import_batches
        WHERE id = NEW.import_batch_id AND firm_id = NEW.firm_id AND client_id = NEW.client_id
          AND engagement_id = NEW.engagement_id FOR SHARE;
      IF origin = 'NATIVE_LEDGER' THEN
        RAISE EXCEPTION 'Native generated GL sources do not accept upload chunks.' USING ERRCODE = '55000';
      END IF;
      RETURN NEW;
    END;
    $$;
    CREATE TRIGGER trg_native_gl_chunk_origin BEFORE INSERT OR UPDATE ON general_ledger_import_chunks
      FOR EACH ROW EXECUTE FUNCTION protect_native_gl_chunks();

    CREATE FUNCTION enforce_gl_origin_facts() RETURNS trigger LANGUAGE plpgsql AS $$
    DECLARE origin text; lifecycle text;
    BEGIN
      IF TG_OP IN ('UPDATE', 'DELETE') THEN
        SELECT origin_kind INTO origin FROM source_import_batches
          WHERE id = OLD.import_batch_id AND firm_id = OLD.firm_id AND client_id = OLD.client_id
            AND engagement_id = OLD.engagement_id FOR SHARE;
        IF origin = 'NATIVE_LEDGER' THEN
          RAISE EXCEPTION 'Generated native GL rows are immutable.' USING ERRCODE = '55000';
        END IF;
        IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
      END IF;
      -- The parent fence also serializes inserts against sealing/reclassification.
      SELECT origin_kind, status INTO origin, lifecycle FROM source_import_batches
        WHERE id = NEW.import_batch_id AND firm_id = NEW.firm_id AND client_id = NEW.client_id
          AND engagement_id = NEW.engagement_id FOR SHARE;
      IF origin IS NULL THEN
        RAISE EXCEPTION 'A GL row requires its exact scoped source batch.' USING ERRCODE = '23503';
      ELSIF origin = 'NATIVE_LEDGER' THEN
        IF lifecycle <> 'LOADING' THEN
          RAISE EXCEPTION 'A sealed native GL population cannot be extended.' USING ERRCODE = '55000';
        END IF;
        IF NEW.is_manual IS NOT NULL OR NEW.is_year_end IS NOT NULL THEN
          RAISE EXCEPTION 'Native GL must retain unavailable source flags as unknown.' USING ERRCODE = '23514';
        END IF;
      ELSIF NEW.is_manual IS NULL OR NEW.is_year_end IS NULL THEN
        RAISE EXCEPTION 'External GL imports require their explicit source flags.' USING ERRCODE = '23514';
      END IF;
      RETURN NEW;
    END;
    $$;
    CREATE TRIGGER trg_native_gl_transaction_origin BEFORE INSERT OR UPDATE OR DELETE ON general_ledger_transactions
      FOR EACH ROW EXECUTE FUNCTION enforce_gl_origin_facts();
    CREATE TRIGGER trg_native_gl_line_origin BEFORE INSERT OR UPDATE OR DELETE ON general_ledger_lines
      FOR EACH ROW EXECUTE FUNCTION enforce_gl_origin_facts();

    CREATE FUNCTION require_complete_native_gl_source() RETURNS trigger LANGUAGE plpgsql AS $$
    DECLARE
      target_id uuid;
      batch source_import_batches%ROWTYPE;
      evidence native_general_ledger_sources%ROWTYPE;
      source trial_balance_datasets%ROWTYPE;
      tb_evidence native_trial_balance_sources%ROWTYPE;
      high_water bigint;
      captured_start date;
      captured_end date;
      expected_transactions bigint;
      expected_lines bigint;
      manifest jsonb;
      opening jsonb;
    BEGIN
      IF TG_TABLE_NAME = 'native_general_ledger_sources' THEN target_id := NEW.import_batch_id;
      ELSE
        IF NEW.origin_kind = 'EXTERNAL' THEN RETURN NULL; END IF;
        target_id := NEW.id;
      END IF;
      SELECT * INTO batch FROM source_import_batches WHERE id = target_id FOR UPDATE;
      SELECT * INTO evidence FROM native_general_ledger_sources WHERE import_batch_id = target_id;
      IF batch.origin_kind IS DISTINCT FROM 'NATIVE_LEDGER' THEN
        IF evidence.import_batch_id IS NOT NULL THEN
          RAISE EXCEPTION 'Native GL provenance requires its native source batch.' USING ERRCODE = '55000';
        END IF;
        RETURN NULL;
      END IF;
      IF batch.status <> 'SEALED' OR evidence.import_batch_id IS NULL
        OR evidence.firm_id <> batch.firm_id OR evidence.client_id <> batch.client_id
        OR evidence.engagement_id <> batch.engagement_id OR evidence.actor_id <> batch.created_by_user_id
        OR evidence.source_identity_hash <> batch.normalized_dataset_digest
        OR batch.row_count <> evidence.line_count OR batch.expected_transaction_count <> evidence.transaction_count
        OR batch.accepted_transaction_count <> evidence.transaction_count
        OR batch.expected_line_count <> evidence.line_count OR batch.accepted_line_count <> evidence.line_count
        OR EXISTS (SELECT 1 FROM general_ledger_import_chunks WHERE import_batch_id = target_id) THEN
        RAISE EXCEPTION 'Native GL must commit sealed with exact scoped evidence/counts and no fabricated upload chunks.' USING ERRCODE = '55000';
      END IF;
      SELECT * INTO source FROM trial_balance_datasets WHERE id = evidence.trial_balance_dataset_id;
      SELECT * INTO tb_evidence FROM native_trial_balance_sources WHERE dataset_id = evidence.trial_balance_dataset_id;
      IF source.source_kind IS DISTINCT FROM 'Native' OR source.import_state IS DISTINCT FROM 'SEALED'
        OR source.firm_id IS DISTINCT FROM batch.firm_id OR source.client_id IS DISTINCT FROM batch.client_id
        OR source.engagement_id IS DISTINCT FROM batch.engagement_id OR source.period_id IS DISTINCT FROM batch.period_id
        OR source.book_id IS NOT NULL OR source.validation_status IS DISTINCT FROM 'Accepted'
        OR source.balanced IS DISTINCT FROM true OR source.control_total IS DISTINCT FROM 0
        OR source.currency IS DISTINCT FROM batch.currency
        OR source.legal_entity_key IS DISTINCT FROM batch.legal_entity_key
        OR evidence.native_trial_balance_source_hash IS DISTINCT FROM source.sha256_hex
        OR evidence.native_trial_balance_manifest_hash IS DISTINCT FROM tb_evidence.manifest_hash
        OR tb_evidence.manifest_json::jsonb->>'Version' IS DISTINCT FROM 'native-tb-source.v1'
        OR tb_evidence.manifest_json::jsonb->>'ActivityOnly' IS DISTINCT FROM 'false'
        OR tb_evidence.manifest_json::jsonb->>'AsOfDate' IS DISTINCT FROM tb_evidence.manifest_json::jsonb->>'PeriodEnd' THEN
        RAISE EXCEPTION 'Native GL requires its exact complete native TB source and opening evidence.' USING ERRCODE = '55000';
      END IF;
      manifest := evidence.manifest_json::jsonb;
      opening := (tb_evidence.manifest_json::jsonb#>>'{Pins,MembershipJson}')::jsonb->'Opening';
      IF manifest->>'Version' IS DISTINCT FROM 'native-gl-source.v1'
        OR manifest->>'FirmId' IS DISTINCT FROM batch.firm_id::text
        OR manifest->>'ClientId' IS DISTINCT FROM batch.client_id::text
        OR manifest->>'EngagementId' IS DISTINCT FROM batch.engagement_id::text
        OR manifest->>'TrialBalanceDatasetId' IS DISTINCT FROM source.id::text
        OR manifest->>'PeriodId' IS DISTINCT FROM batch.period_id::text
        OR manifest->>'NativeTrialBalanceSourceHash' IS DISTINCT FROM evidence.native_trial_balance_source_hash
        OR manifest->>'NativeTrialBalanceManifestHash' IS DISTINCT FROM evidence.native_trial_balance_manifest_hash
        OR manifest->>'NativeTrialBalanceManifestJson' IS DISTINCT FROM tb_evidence.manifest_json
        OR manifest->>'AsOfDate' IS DISTINCT FROM tb_evidence.manifest_json::jsonb->>'AsOfDate'
        OR manifest->>'Currency' IS DISTINCT FROM batch.currency
        OR manifest->>'LegalEntityKey' IS DISTINCT FROM batch.legal_entity_key
        OR manifest->>'BookScope' IS DISTINCT FROM 'PERIOD_LEDGER_NO_BOOK'
        OR manifest->'PostingSequence' IS DISTINCT FROM tb_evidence.manifest_json::jsonb#>'{Pins,PostingSequence}'
        OR manifest#>>'{NativeOpening,OpeningId}' IS DISTINCT FROM opening->>'Id'
        OR manifest#>>'{NativeOpening,ManifestHash}' IS DISTINCT FROM opening->>'ManifestSha256'
        OR opening IS NULL OR opening = 'null'::jsonb
        OR manifest->'TransactionCount' IS DISTINCT FROM to_jsonb(evidence.transaction_count)
        OR manifest->'LineCount' IS DISTINCT FROM to_jsonb(evidence.line_count)
        OR manifest->>'GeneratedRowsHash' IS DISTINCT FROM evidence.generated_rows_hash
        OR manifest->>'EvidenceReference' IS DISTINCT FROM batch.receipt_reference
        OR manifest->'UnavailableMetadata' IS DISTINCT FROM
          '["IsManual","IsYearEnd","DocumentDate","ServiceDate","ReversalReference","PartyIdentifier","Branch","CostCentre","Department","Project","IntercompanyCounterparty"]'::jsonb THEN
        RAISE EXCEPTION 'Native GL manifest must retain its exact native TB and source facts.' USING ERRCODE = '55000';
      END IF;
      IF NOT EXISTS (SELECT 1 FROM source_acceptance_decisions d
        WHERE d.firm_id = batch.firm_id AND d.client_id = batch.client_id AND d.engagement_id = batch.engagement_id
          AND d.trial_balance_dataset_id = source.id AND d.source_kind = 'TB' AND d.decision = 'ACCEPTED'
          AND d.source_identity_hash = source.sha256_hex AND d.accepted_by_user_id <> source.imported_by_user_id) THEN
        RAISE EXCEPTION 'Native GL requires retained independent acceptance of its exact native TB source.' USING ERRCODE = '55000';
      END IF;
      high_water := (tb_evidence.manifest_json::jsonb#>>'{Pins,PostingSequence}')::bigint;
      captured_start := (tb_evidence.manifest_json::jsonb->>'PeriodStart')::date;
      captured_end := (tb_evidence.manifest_json::jsonb->>'AsOfDate')::date;
      IF high_water IS NULL OR high_water < 1 OR captured_start IS NULL OR captured_end IS NULL THEN
        RAISE EXCEPTION 'Native GL requires actual posted source movements, not generated opening journals.' USING ERRCODE = '55000';
      END IF;
      SELECT count(*) INTO expected_transactions FROM client_operational_journals j
        WHERE j.firm_id = batch.firm_id AND j.client_id = batch.client_id AND j.period_id = batch.period_id
          AND j.status = 'POSTED' AND j.posting_sequence <= high_water AND j.posting_date BETWEEN captured_start AND captured_end;
      SELECT count(*) INTO expected_lines FROM client_operational_journal_lines l
        JOIN client_operational_journals j ON j.id = l.journal_id AND j.firm_id = l.firm_id AND j.client_id = l.client_id
        WHERE j.firm_id = batch.firm_id AND j.client_id = batch.client_id AND j.period_id = batch.period_id
          AND j.status = 'POSTED' AND j.posting_sequence <= high_water AND j.posting_date BETWEEN captured_start AND captured_end;
      IF evidence.transaction_count <> expected_transactions OR evidence.line_count <> expected_lines
        OR evidence.transaction_count <> (SELECT count(*) FROM general_ledger_transactions WHERE import_batch_id = target_id)
        OR evidence.line_count <> (SELECT count(*) FROM general_ledger_lines WHERE import_batch_id = target_id) THEN
        RAISE EXCEPTION 'Native GL must retain the complete exact posted source membership.' USING ERRCODE = '55000';
      END IF;
      IF EXISTS (
        SELECT 1 FROM general_ledger_transactions g
        LEFT JOIN client_operational_journals j ON j.id::text = g.stable_journal_id
          AND j.firm_id = g.firm_id AND j.client_id = g.client_id AND j.period_id = batch.period_id
        WHERE g.import_batch_id = target_id AND (
          j.id IS NULL OR j.status <> 'POSTED' OR j.posting_sequence IS NULL OR j.posting_sequence > high_water
          OR j.posting_date NOT BETWEEN captured_start AND captured_end OR g.posting_date <> j.posting_date
          OR g.document_number <> j.journal_number OR g.currency <> j.currency OR g.currency <> batch.currency
          OR g.source_user <> j.created_by_user_id::text OR g.source_system <> 'AUDITSPHERE_NATIVE'
          OR g.document_date IS NOT NULL OR g.service_date IS NOT NULL
          OR g.is_manual IS NOT NULL OR g.is_year_end IS NOT NULL
          OR (g.reversal_reference IS NOT NULL AND NOT EXISTS (
            SELECT 1 FROM client_operational_journal_reversals r WHERE r.firm_id = batch.firm_id
              AND r.client_id = batch.client_id AND r.reversal_journal_id = j.id AND r.original_journal_id::text = g.reversal_reference)))) THEN
        RAISE EXCEPTION 'A native GL transaction must reproduce actual recorded native journal facts.' USING ERRCODE = '55000';
      END IF;
      IF EXISTS (
        SELECT 1 FROM general_ledger_lines g
        LEFT JOIN general_ledger_transactions t ON t.id = g.transaction_id AND t.import_batch_id = g.import_batch_id
        LEFT JOIN client_operational_journal_lines l ON l.id::text = g.stable_line_id
          AND l.firm_id = g.firm_id AND l.client_id = g.client_id AND l.journal_id::text = t.stable_journal_id
        WHERE g.import_batch_id = target_id AND (
          l.id IS NULL OR g.client_account_id IS DISTINCT FROM l.client_account_id OR g.account_code <> l.account_code
          OR g.debit <> l.debit OR g.credit <> l.credit OR g.original_currency <> t.currency
          OR g.original_amount <> l.debit - l.credit OR g.functional_amount <> l.debit - l.credit
          OR g.is_manual IS NOT NULL OR g.is_year_end IS NOT NULL)) THEN
        RAISE EXCEPTION 'A native GL line must reproduce its actual native source line and account.' USING ERRCODE = '55000';
      END IF;
      IF manifest->'TotalDebit' IS DISTINCT FROM to_jsonb((SELECT sum(debit) FROM general_ledger_lines WHERE import_batch_id = target_id))
        OR manifest->'TotalCredit' IS DISTINCT FROM to_jsonb((SELECT sum(credit) FROM general_ledger_lines WHERE import_batch_id = target_id))
        OR (SELECT count(DISTINCT value->>'LineId') FROM jsonb_array_elements(evidence.generated_rows_json::jsonb)) <> evidence.line_count
        OR (SELECT count(DISTINCT value->>'JournalId') FROM jsonb_array_elements(evidence.generated_rows_json::jsonb)) <> evidence.transaction_count
        OR EXISTS (
          SELECT 1 FROM jsonb_array_elements(evidence.generated_rows_json::jsonb) r(value)
          LEFT JOIN general_ledger_lines l ON l.import_batch_id = target_id AND l.stable_line_id = r.value->>'LineId'
          LEFT JOIN general_ledger_transactions j ON j.id = l.transaction_id AND j.import_batch_id = target_id
          WHERE l.id IS NULL OR j.id IS NULL
            OR r.value->>'JournalId' IS DISTINCT FROM j.stable_journal_id
            OR r.value->>'ClientAccountId' IS DISTINCT FROM l.client_account_id::text
            OR r.value->>'JournalNumber' IS DISTINCT FROM j.document_number
            OR r.value->>'PostingDate' IS DISTINCT FROM to_char(j.posting_date, 'YYYY-MM-DD')
            OR r.value->>'AccountCode' IS DISTINCT FROM l.account_code
            OR (r.value->>'Debit')::numeric IS DISTINCT FROM l.debit
            OR (r.value->>'Credit')::numeric IS DISTINCT FROM l.credit
            OR r.value->>'OriginalCurrency' IS DISTINCT FROM l.original_currency
            OR (r.value->>'OriginalAmount')::numeric IS DISTINCT FROM l.original_amount
            OR (r.value->>'FunctionalAmount')::numeric IS DISTINCT FROM l.functional_amount
            OR r.value->>'Currency' IS DISTINCT FROM j.currency
            OR r.value->>'SourceUser' IS DISTINCT FROM j.source_user
            OR r.value->>'SourceSystem' IS DISTINCT FROM j.source_system
            OR r.value->'IsManual' IS DISTINCT FROM 'null'::jsonb
            OR r.value->'IsYearEnd' IS DISTINCT FROM 'null'::jsonb) THEN
        RAISE EXCEPTION 'Native GL generated artifact must reproduce exact retained source rows and totals.' USING ERRCODE = '55000';
      END IF;
      RETURN NULL;
    END;
    $$;
    CREATE CONSTRAINT TRIGGER trg_native_gl_evidence_complete AFTER INSERT ON native_general_ledger_sources
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION require_complete_native_gl_source();
    CREATE CONSTRAINT TRIGGER trg_native_gl_batch_complete AFTER INSERT OR UPDATE ON source_import_batches
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION require_complete_native_gl_source();
    """;

  internal const string Remove = """
    DO $$ BEGIN
      IF EXISTS (SELECT 1 FROM native_general_ledger_sources)
        OR EXISTS (SELECT 1 FROM source_import_batches WHERE origin_kind = 'NATIVE_LEDGER') THEN
        RAISE EXCEPTION 'Native GL history must be retained; rollback is refused.' USING ERRCODE = '55000';
      END IF;
    END $$;
    DROP TRIGGER trg_native_gl_evidence_complete ON native_general_ledger_sources;
    DROP TRIGGER trg_native_gl_batch_complete ON source_import_batches;
    DROP FUNCTION require_complete_native_gl_source();
    DROP TRIGGER trg_native_gl_transaction_origin ON general_ledger_transactions;
    DROP TRIGGER trg_native_gl_line_origin ON general_ledger_lines;
    DROP FUNCTION enforce_gl_origin_facts();
    DROP TRIGGER trg_native_gl_chunk_origin ON general_ledger_import_chunks;
    DROP FUNCTION protect_native_gl_chunks();
    DROP TRIGGER trg_native_gl_header_immutable ON source_import_batches;
    DROP FUNCTION protect_native_gl_header();
    DROP TRIGGER trg_native_gl_evidence_truncate ON native_general_ledger_sources;
    DROP TRIGGER trg_native_gl_batches_truncate ON source_import_batches;
    DROP TRIGGER trg_native_gl_transactions_truncate ON general_ledger_transactions;
    DROP TRIGGER trg_native_gl_lines_truncate ON general_ledger_lines;
    DROP FUNCTION protect_native_gl_truncate();
    DROP TRIGGER trg_native_gl_evidence_immutable ON native_general_ledger_sources;
    DROP FUNCTION protect_native_gl_evidence();
    """;
}
