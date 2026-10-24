namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

internal static class ConfirmationBookedSourceSql
{
  internal const string Up = """
    CREATE FUNCTION protect_confirmation_booked_source() RETURNS trigger LANGUAGE plpgsql AS $fn$
    BEGIN
      RAISE EXCEPTION 'Confirmation booked-source evidence is append-only.' USING ERRCODE='55000';
    END; $fn$;
    CREATE TRIGGER confirmation_booked_source_immutable BEFORE UPDATE OR DELETE
      ON audit_confirmation_booked_sources FOR EACH ROW EXECUTE FUNCTION protect_confirmation_booked_source();
    CREATE TRIGGER confirmation_booked_source_no_truncate BEFORE TRUNCATE
      ON audit_confirmation_booked_sources FOR EACH STATEMENT EXECUTE FUNCTION protect_confirmation_booked_source();

    CREATE FUNCTION protect_source_bound_confirmation() RETURNS trigger LANGUAGE plpgsql AS $fn$
    BEGIN
      IF (NEW.firm_id,NEW.client_id,NEW.engagement_id,NEW.procedure_id,NEW.area_code,
          NEW.source_record_id,NEW.booked_amount,NEW.currency,NEW.confirmation_date,NEW.respondent,
          NEW.contact_validation_source,NEW.input_generation,NEW.created_by_user_id,NEW.created_at)
         IS DISTINCT FROM
         (OLD.firm_id,OLD.client_id,OLD.engagement_id,OLD.procedure_id,OLD.area_code,
          OLD.source_record_id,OLD.booked_amount,OLD.currency,OLD.confirmation_date,OLD.respondent,
          OLD.contact_validation_source,OLD.input_generation,OLD.created_by_user_id,OLD.created_at)
         AND EXISTS (SELECT 1 FROM audit_confirmation_booked_sources s WHERE s.confirmation_case_id=OLD.id) THEN
        RAISE EXCEPTION 'A source-bound confirmation cannot rebind its original financial or source identity.' USING ERRCODE='55000';
      END IF;
      RETURN NEW;
    END; $fn$;
    CREATE TRIGGER source_bound_confirmation_identity BEFORE UPDATE ON audit_confirmation_cases
      FOR EACH ROW EXECUTE FUNCTION protect_source_bound_confirmation();

    CREATE FUNCTION verify_confirmation_booked_source() RETURNS trigger LANGUAGE plpgsql AS $fn$
    DECLARE
      c audit_confirmation_cases%ROWTYPE;
      bank audit_bank_reconciliations%ROWTYPE;
      ledger audit_schedules%ROWTYPE;
      statement audit_schedules%ROWTYPE;
      batch source_import_batches%ROWTYPE;
      generation bigint;
      doc jsonb := NEW.snapshot_json::jsonb;
      bank_doc jsonb := doc->'Bank';
      schedule_doc jsonb;
      batch_doc jsonb;
      schedule_row audit_schedules%ROWTYPE;
    BEGIN
      SELECT * INTO c FROM audit_confirmation_cases WHERE id=NEW.confirmation_case_id
        AND firm_id=NEW.firm_id AND client_id=NEW.client_id AND engagement_id=NEW.engagement_id;
      IF NOT FOUND OR c.area_code<>'CASH_BANK' OR c.status<>'DRAFT'
         OR c.created_by_user_id<>NEW.created_by_user_id OR c.created_at<>NEW.created_at THEN
        RAISE EXCEPTION 'Booked-source evidence must belong to its original new Bank case.' USING ERRCODE='23514';
      END IF;
      SELECT input_generation INTO generation FROM client_safety_states
        WHERE id=NEW.client_id AND firm_id=NEW.firm_id FOR SHARE;
      IF NOT FOUND OR generation<>c.input_generation THEN
        RAISE EXCEPTION 'Booked-source evidence requires exact current client inputs.' USING ERRCODE='23514';
      END IF;
      SELECT * INTO bank FROM audit_bank_reconciliations WHERE id=NEW.bank_reconciliation_id
        AND firm_id=NEW.firm_id AND client_id=NEW.client_id AND engagement_id=NEW.engagement_id FOR UPDATE;
      IF NOT FOUND OR bank.status<>'APPROVED' OR bank.residual<>0
         OR bank.reviewed_by_user_id IS NULL OR bank.reviewed_at IS NULL
         OR bank.created_by_user_id=bank.reviewed_by_user_id
         OR bank.input_generation<>generation OR bank.ledger_schedule_id<>NEW.ledger_schedule_id
         OR bank.statement_schedule_id<>NEW.statement_schedule_id
         OR c.booked_amount IS DISTINCT FROM bank.ledger_balance
         OR c.currency IS DISTINCT FROM bank.currency THEN
        RAISE EXCEPTION 'Booked amount must be the exact approved current Bank ledger balance and currency.' USING ERRCODE='23514';
      END IF;
      IF NOT COALESCE(bank_doc @> jsonb_build_object('Id',bank.id,'FirmId',bank.firm_id,
           'ClientId',bank.client_id,'EngagementId',bank.engagement_id,'ProcedureId',bank.procedure_id,
           'LedgerScheduleId',bank.ledger_schedule_id,'StatementScheduleId',bank.statement_schedule_id,
           'AsOfDate',bank.as_of_date,'Currency',bank.currency,'InputGeneration',bank.input_generation,
           'Status',bank.status,'CreatedByUserId',bank.created_by_user_id,
           'ReviewedByUserId',bank.reviewed_by_user_id,'Conclusion',bank.conclusion),false)
         OR (bank_doc->>'LedgerBalance')::numeric IS DISTINCT FROM bank.ledger_balance
         OR (bank_doc->>'StatementBalance')::numeric IS DISTINCT FROM bank.statement_balance
         OR (bank_doc->>'TimingItemTotal')::numeric IS DISTINCT FROM bank.timing_item_total
         OR (bank_doc->>'ProposedCorrectionTotal')::numeric IS DISTINCT FROM bank.proposed_correction_total
         OR (bank_doc->>'Residual')::numeric IS DISTINCT FROM bank.residual
         OR (bank_doc->>'CreatedAt')::timestamptz IS DISTINCT FROM bank.created_at
         OR (bank_doc->>'ReviewedAt')::timestamptz IS DISTINCT FROM bank.reviewed_at
         OR NOT COALESCE((doc->>'LedgerRowsSha256') ~ '^[0-9a-f]{64}$',false)
         OR NOT COALESCE((doc->>'StatementRowsSha256') ~ '^[0-9a-f]{64}$',false)
         OR jsonb_typeof(doc->'Items') IS DISTINCT FROM 'array' THEN
        RAISE EXCEPTION 'Booked-source snapshot must retain actual Bank source facts.' USING ERRCODE='23514';
      END IF;
      PERFORM id FROM audit_bank_reconciliation_items WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id
        AND engagement_id=NEW.engagement_id AND bank_reconciliation_id=bank.id ORDER BY id FOR SHARE;
      IF jsonb_array_length(doc->'Items') <> (SELECT count(*) FROM audit_bank_reconciliation_items i
           WHERE i.firm_id=NEW.firm_id AND i.client_id=NEW.client_id
             AND i.engagement_id=NEW.engagement_id AND i.bank_reconciliation_id=bank.id)
         OR EXISTS (SELECT 1 FROM jsonb_array_elements(doc->'Items') j
           GROUP BY j->>'Id' HAVING count(*)<>1)
         OR EXISTS (SELECT 1 FROM jsonb_array_elements(doc->'Items') j
           WHERE NOT EXISTS (SELECT 1 FROM audit_bank_reconciliation_items i
             WHERE i.id=(j->>'Id')::uuid AND i.firm_id=NEW.firm_id AND i.client_id=NEW.client_id
               AND i.engagement_id=NEW.engagement_id AND i.bank_reconciliation_id=bank.id
               AND j @> jsonb_build_object('SourceScheduleId',i.source_schedule_id,
                 'ProposedJournalId',i.proposed_journal_id,'StableItemId',i.stable_item_id,
                 'ItemType',i.item_type,'Currency',i.currency,'Description',i.description,
                 'SourceReference',i.source_reference,'EvidenceReference',i.evidence_reference)
               AND (j->>'SignedAmount')::numeric IS NOT DISTINCT FROM i.signed_amount
               AND (j->>'CreatedAt')::timestamptz IS NOT DISTINCT FROM i.created_at)) THEN
        RAISE EXCEPTION 'Booked-source snapshot must retain every exact Bank item.' USING ERRCODE='23514';
      END IF;
      -- Both writers and this direct-SQL boundary acquire schedule rows in one stable order.
      PERFORM id FROM audit_schedules WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id
        AND engagement_id=NEW.engagement_id AND id IN (NEW.ledger_schedule_id,NEW.statement_schedule_id)
        ORDER BY id FOR UPDATE;
      PERFORM id FROM audit_schedule_rows WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id
        AND engagement_id=NEW.engagement_id AND schedule_id IN (NEW.ledger_schedule_id,NEW.statement_schedule_id)
        ORDER BY id FOR SHARE;
      SELECT * INTO ledger FROM audit_schedules WHERE id=NEW.ledger_schedule_id
        AND firm_id=NEW.firm_id AND client_id=NEW.client_id AND engagement_id=NEW.engagement_id;
      IF NOT FOUND THEN RAISE EXCEPTION 'Exact Bank ledger schedule required.' USING ERRCODE='23514'; END IF;
      SELECT * INTO statement FROM audit_schedules WHERE id=NEW.statement_schedule_id
        AND firm_id=NEW.firm_id AND client_id=NEW.client_id AND engagement_id=NEW.engagement_id;
      IF NOT FOUND THEN RAISE EXCEPTION 'Exact Bank statement schedule required.' USING ERRCODE='23514'; END IF;
      IF ledger.schedule_type<>'BANK_LEDGER' OR statement.schedule_type<>'BANK_STATEMENT'
         OR ledger.source_import_batch_id IS NULL OR ledger.signed_control_total<>bank.ledger_balance
         OR statement.signed_control_total<>bank.statement_balance THEN
        RAISE EXCEPTION 'Booked-source evidence requires its exact linked Bank schedules.' USING ERRCODE='23514';
      END IF;
      FOR schedule_row IN SELECT * FROM audit_schedules WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id
        AND engagement_id=NEW.engagement_id AND id IN (NEW.ledger_schedule_id,NEW.statement_schedule_id) ORDER BY id LOOP
        schedule_doc := CASE WHEN schedule_row.id=NEW.ledger_schedule_id THEN doc->'LedgerSchedule' ELSE doc->'StatementSchedule' END;
        batch_doc := CASE WHEN schedule_row.id=NEW.ledger_schedule_id THEN doc->'LedgerBatch' ELSE doc->'StatementBatch' END;
        IF schedule_row.status<>'APPROVED' OR schedule_row.input_generation<>generation
           OR schedule_row.currency<>bank.currency OR schedule_row.as_of_date IS NULL
           OR (schedule_row.as_of_date AT TIME ZONE 'UTC')::date<>bank.as_of_date
           OR NOT COALESCE(schedule_doc @> jsonb_build_object('Id',schedule_row.id,
             'FirmId',schedule_row.firm_id,'ClientId',schedule_row.client_id,'EngagementId',schedule_row.engagement_id,
             'ScheduleType',schedule_row.schedule_type,'EntityIdentifier',schedule_row.entity_identifier,
             'SourceReceiptReference',schedule_row.source_receipt_reference,'Currency',schedule_row.currency,
             'SignConvention',schedule_row.sign_convention,'SourceHash',schedule_row.source_hash,
             'SourceImportBatchId',schedule_row.source_import_batch_id,'AcceptedSourceDecisionId',schedule_row.accepted_source_decision_id,
             'AcceptedSourceHash',schedule_row.accepted_source_hash,'RowCount',schedule_row.row_count,
             'InputGeneration',schedule_row.input_generation,'CompletenessDecision',schedule_row.completeness_decision,
             'Status',schedule_row.status,'CreatedByUserId',schedule_row.created_by_user_id,
             'PeriodStart',schedule_row.period_start,'PeriodEnd',schedule_row.period_end),false)
           OR (schedule_doc->>'AsOfDate')::timestamptz IS DISTINCT FROM schedule_row.as_of_date
           OR (schedule_doc->>'CreatedAt')::timestamptz IS DISTINCT FROM schedule_row.created_at
           OR (schedule_doc->>'SignedControlTotal')::numeric IS DISTINCT FROM schedule_row.signed_control_total
           OR (schedule_doc->>'GlControlTotal')::numeric IS DISTINCT FROM schedule_row.gl_control_total
           OR (schedule_doc->>'Residual')::numeric IS DISTINCT FROM schedule_row.residual THEN
          RAISE EXCEPTION 'Booked-source snapshot must bind exact current approved schedule facts.' USING ERRCODE='23514';
        END IF;
        IF schedule_row.accepted_source_decision_id IS NULL THEN
          IF length(COALESCE(schedule_row.accepted_source_hash,''))<>0 THEN
            RAISE EXCEPTION 'An absent accepted-source decision is not accepted evidence.' USING ERRCODE='23514';
          END IF;
        ELSIF NOT EXISTS (SELECT 1 FROM source_acceptance_decisions a WHERE a.id=schedule_row.accepted_source_decision_id
          AND a.firm_id=NEW.firm_id AND a.client_id=NEW.client_id AND a.engagement_id=NEW.engagement_id
          AND a.decision='ACCEPTED' AND a.source_identity_hash=schedule_row.accepted_source_hash) THEN
          RAISE EXCEPTION 'Exact optional accepted-source evidence required when bound.' USING ERRCODE='23514';
        END IF;
        IF schedule_row.source_import_batch_id IS NULL THEN
          IF batch_doc IS DISTINCT FROM 'null'::jsonb THEN
            RAISE EXCEPTION 'An absent source batch cannot be invented.' USING ERRCODE='23514';
          END IF;
        ELSE
          SELECT * INTO batch FROM source_import_batches WHERE id=schedule_row.source_import_batch_id
            AND firm_id=NEW.firm_id AND client_id=NEW.client_id AND engagement_id=NEW.engagement_id FOR SHARE;
          IF NOT FOUND OR batch.status<>'SEALED' OR batch.source_kind<>'GL'
             OR batch.currency<>bank.currency OR batch.legal_entity_key<>schedule_row.entity_identifier
             OR (schedule_row.source_hash<>batch.raw_file_sha256_hex AND schedule_row.source_hash<>batch.normalized_dataset_digest)
             OR NOT COALESCE(batch_doc @> jsonb_build_object('Id',batch.id,'FirmId',batch.firm_id,
               'ClientId',batch.client_id,'EngagementId',batch.engagement_id,'PeriodId',batch.period_id,'BookId',batch.book_id,
               'SourceKind',batch.source_kind,'OriginKind',batch.origin_kind,'Status',batch.status,'Currency',batch.currency,
               'LegalEntityKey',batch.legal_entity_key,'RawFileSha256Hex',batch.raw_file_sha256_hex,
               'NormalizedDatasetDigest',batch.normalized_dataset_digest,'ReceiptReference',batch.receipt_reference,
               'ProfileVersion',batch.profile_version,'ParserVersion',batch.parser_version,
               'RowCount',batch.row_count,'ExpectedChunkCount',batch.expected_chunk_count,
               'ExpectedTransactionCount',batch.expected_transaction_count,'ExpectedLineCount',batch.expected_line_count,
               'AcceptedChunkCount',batch.accepted_chunk_count,'AcceptedTransactionCount',batch.accepted_transaction_count,
               'AcceptedLineCount',batch.accepted_line_count,'CreatedByUserId',batch.created_by_user_id),false)
             OR (batch_doc->>'CreatedAt')::timestamptz IS DISTINCT FROM batch.created_at THEN
            RAISE EXCEPTION 'Booked-source snapshot requires its actual sealed scoped source batch.' USING ERRCODE='23514';
          END IF;
        END IF;
      END LOOP;
      RETURN NEW;
    END; $fn$;
    CREATE CONSTRAINT TRIGGER confirmation_booked_source_binding AFTER INSERT ON audit_confirmation_booked_sources
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION verify_confirmation_booked_source();
    """;

  internal const string Down = """
    DO $fn$ BEGIN
      IF EXISTS (SELECT 1 FROM audit_confirmation_booked_sources) THEN
        RAISE EXCEPTION 'Cannot remove retained confirmation booked-source evidence.' USING ERRCODE='55000';
      END IF;
    END; $fn$;
    DROP TRIGGER confirmation_booked_source_binding ON audit_confirmation_booked_sources;
    DROP FUNCTION verify_confirmation_booked_source();
    DROP TRIGGER source_bound_confirmation_identity ON audit_confirmation_cases;
    DROP FUNCTION protect_source_bound_confirmation();
    DROP TRIGGER confirmation_booked_source_no_truncate ON audit_confirmation_booked_sources;
    DROP TRIGGER confirmation_booked_source_immutable ON audit_confirmation_booked_sources;
    DROP FUNCTION protect_confirmation_booked_source();
    """;
}
