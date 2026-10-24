namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

// Install after the companion tables. Physical byte verification, current authorization and
// complete native ancestry/currentness remain application guards under the shared scope locks.
internal static class AuditScheduleSourceIntakeSql
{
  internal const string Install = """
    CREATE FUNCTION audit_schedule_source_money(value text) RETURNS numeric LANGUAGE plpgsql IMMUTABLE AS $fn$
    DECLARE amount numeric;
    BEGIN
      IF value IS NULL OR value !~ '^-?[0-9]{1,13}\.[0-9]{6}$' THEN
        RAISE EXCEPTION 'Exact bounded schedule money is required.' USING ERRCODE='23514';
      END IF;
      amount := value::numeric;
      IF abs(amount)>9999999999999.999999 THEN
        RAISE EXCEPTION 'Schedule money exceeds numeric(19,6).' USING ERRCODE='23514';
      END IF;
      RETURN amount;
    END; $fn$;

    -- Match producer PascalCase scalar witnesses to actual snake_case database rows.
    -- Numeric strings and UTC timestamp spellings are compared by value, never by
    -- reserializing jsonb and pretending its bytes are the original producer hash.
    CREATE FUNCTION audit_schedule_source_matches(actual jsonb, witness jsonb) RETURNS boolean
      LANGUAGE plpgsql STABLE AS $fn$
    DECLARE pair record; column_name text; stored jsonb;
    BEGIN
      IF jsonb_typeof(actual) IS DISTINCT FROM 'object' OR jsonb_typeof(witness) IS DISTINCT FROM 'object' THEN RETURN false; END IF;
      FOR pair IN SELECT key,value FROM jsonb_each(witness) LOOP
        column_name := lower(regexp_replace(pair.key,'([a-z0-9])([A-Z])','\1_\2','g'));
        IF NOT actual ? column_name THEN RETURN false; END IF;
        stored := actual->column_name;
        IF stored='null'::jsonb OR pair.value='null'::jsonb THEN
          IF stored IS DISTINCT FROM pair.value THEN RETURN false; END IF;
        ELSIF column_name LIKE '%\_at' ESCAPE '\' THEN
          IF (stored#>>'{}')::timestamptz IS DISTINCT FROM (pair.value#>>'{}')::timestamptz THEN RETURN false; END IF;
        ELSIF jsonb_typeof(stored)='number' THEN
          IF (stored#>>'{}')::numeric IS DISTINCT FROM (pair.value#>>'{}')::numeric THEN RETURN false; END IF;
        ELSIF stored IS DISTINCT FROM pair.value THEN RETURN false;
        END IF;
      END LOOP;
      RETURN true;
    END; $fn$;

    CREATE FUNCTION audit_schedule_source_current(c audit_schedule_source_captures) RETURNS void
      LANGUAGE plpgsql AS $fn$
    DECLARE m jsonb := c.manifest_json::jsonb;
      accepted source_acceptance_decisions%ROWTYPE; dataset trial_balance_datasets%ROWTYPE;
      period_row client_reporting_periods%ROWTYPE; book_row client_reporting_books%ROWTYPE;
      receipt pbc_acceptance_receipts%ROWTYPE; request_row pbc_requests%ROWTYPE;
      upload pbc_upload_intents%ROWTYPE; operation_row durable_operations%ROWTYPE;
      current_decision uuid; generation bigint; native native_trial_balance_sources%ROWTYPE;
    BEGIN
      -- Same first locks as the publication/source-acceptance writers, before source rows.
      PERFORM id FROM firm_safety_states WHERE id=c.firm_id FOR SHARE;
      IF NOT FOUND THEN RAISE EXCEPTION 'Schedule firm scope is unavailable.' USING ERRCODE='23514'; END IF;
      SELECT input_generation INTO generation FROM client_safety_states
        WHERE firm_id=c.firm_id AND id=c.client_id FOR UPDATE;
      IF NOT FOUND OR generation<>c.input_generation THEN
        RAISE EXCEPTION 'Schedule source generation changed.' USING ERRCODE='23514';
      END IF;
      PERFORM id FROM engagements WHERE firm_id=c.firm_id AND id=c.engagement_id AND practice_client_id=c.client_id FOR UPDATE;
      IF NOT FOUND THEN RAISE EXCEPTION 'Schedule engagement scope changed.' USING ERRCODE='23514'; END IF;
      SELECT * INTO receipt FROM pbc_acceptance_receipts WHERE id=c.pbc_acceptance_receipt_id
        AND firm_id=c.firm_id AND client_id=c.client_id AND engagement_id=c.engagement_id;
      IF NOT FOUND OR NOT audit_schedule_source_matches(to_jsonb(receipt),m#>'{Pbc,Receipt}') OR
         NOT COALESCE((m#>'{Pbc,Receipt}') @> jsonb_build_object('Id',receipt.id,'SourceDigest',receipt.source_digest,
           'ContentSha256Hex',receipt.content_sha256_hex,'ByteCount',receipt.byte_count),false) THEN
        RAISE EXCEPTION 'Schedule must retain its actual accepted PBC receipt.' USING ERRCODE='23514';
      END IF;
      SELECT * INTO request_row FROM pbc_requests WHERE id=receipt.pbc_request_id AND firm_id=c.firm_id
        AND client_id=c.client_id AND engagement_id=c.engagement_id FOR SHARE;
      IF NOT FOUND OR request_row.state NOT IN ('ACCEPTED','CLOSED') OR
         NOT (request_row.state='ACCEPTED' AND request_row.revision=receipt.accepted_request_revision OR
           request_row.state='CLOSED' AND receipt.accepted_request_revision<9223372036854775807 AND
           request_row.revision=receipt.accepted_request_revision+1) OR
         request_row.accepted_by_user_id IS DISTINCT FROM receipt.reviewer_user_id OR
         request_row.reviewer_user_id IS DISTINCT FROM receipt.reviewer_user_id OR
         request_row.accepted_at IS DISTINCT FROM receipt.accepted_at OR
         request_row.entity_scope<>c.legal_entity_key OR
         (m#>'{Pbc,Request}') ?| ARRAY['State','Revision'] OR
         NOT audit_schedule_source_matches(to_jsonb(request_row),m#>'{Pbc,Request}') THEN
        RAISE EXCEPTION 'Accepted PBC source context or professional identity changed.' USING ERRCODE='23514';
      END IF;
      SELECT * INTO upload FROM pbc_upload_intents WHERE id=receipt.pbc_upload_intent_id
        AND firm_id=c.firm_id AND client_id=c.client_id AND engagement_id=c.engagement_id AND pbc_request_id=request_row.id;
      IF NOT FOUND OR upload.state<>'RECEIVED' OR upload.revision<>receipt.upload_intent_revision OR
         upload.request_revision<>receipt.source_request_revision OR upload.uploader_user_id<>receipt.uploader_user_id OR
         receipt.reviewer_user_id=receipt.uploader_user_id OR upload.file_name<>receipt.file_name OR
         upload.declared_byte_count<>receipt.byte_count OR upload.received_byte_count<>receipt.byte_count OR
         upload.final_sha256_hex IS DISTINCT FROM receipt.content_sha256_hex OR
         upload.declared_sha256_hex IS DISTINCT FROM receipt.content_sha256_hex OR
         upload.provider_receipt_digest IS DISTINCT FROM receipt.provider_receipt_digest OR
         receipt.provider_receipt_digest<>receipt.content_sha256_hex OR
         upload.transfer_operation_id IS DISTINCT FROM receipt.transfer_operation_id OR
         upload.provider_registered_at IS DISTINCT FROM receipt.provider_registered_at OR
         NOT audit_schedule_source_matches(to_jsonb(upload),m#>'{Pbc,Upload}') THEN
        RAISE EXCEPTION 'Schedule PBC upload must be the exact accepted verified version.' USING ERRCODE='23514';
      END IF;
      SELECT * INTO operation_row FROM durable_operations WHERE id=receipt.transfer_operation_id AND firm_id=c.firm_id;
      IF NOT FOUND OR operation_row.operation_kind<>'TransferPbcDocument.v1' OR operation_row.status<>'COMPLETED' OR
         operation_row.target_id<>upload.id OR operation_row.client_id<>c.client_id OR operation_row.engagement_id<>c.engagement_id OR
         operation_row.expected_revision<>upload.revision-1 OR operation_row.result_identity IS DISTINCT FROM receipt.provider_identity OR
         operation_row.result_digest IS DISTINCT FROM receipt.content_sha256_hex OR
         NOT audit_schedule_source_matches(to_jsonb(operation_row),m#>'{Pbc,Transfer}') THEN
        RAISE EXCEPTION 'The accepted schedule transfer witness changed.' USING ERRCODE='23514';
      END IF;
      IF receipt.byte_count NOT BETWEEN 1 AND 10000000 OR receipt.file_name !~* '\.csv$' OR
         m->>'RawFileSha256' IS DISTINCT FROM receipt.content_sha256_hex OR
         (m->>'ByteCount')::bigint IS DISTINCT FROM receipt.byte_count THEN
        RAISE EXCEPTION 'The schedule file identity is not the bounded accepted CSV.' USING ERRCODE='23514';
      END IF;
      SELECT * INTO accepted FROM source_acceptance_decisions WHERE id=c.accepted_source_decision_id
        AND firm_id=c.firm_id AND client_id=c.client_id AND engagement_id=c.engagement_id;
      IF NOT FOUND OR accepted.source_kind<>'TB' OR accepted.decision<>'ACCEPTED' OR
         accepted.trial_balance_dataset_id IS DISTINCT FROM c.trial_balance_dataset_id OR accepted.import_batch_id IS NOT NULL OR
         NOT audit_schedule_source_matches(to_jsonb(accepted),m#>'{TrialBalance,Decision}') THEN
        RAISE EXCEPTION 'An exact accepted TB decision is required.' USING ERRCODE='23514';
      END IF;
      SELECT * INTO dataset FROM trial_balance_datasets WHERE id=c.trial_balance_dataset_id AND firm_id=c.firm_id
        AND client_id=c.client_id AND engagement_id=c.engagement_id;
      IF NOT FOUND OR dataset.import_state<>'SEALED' OR dataset.validation_status<>'Accepted' OR NOT dataset.balanced OR
         dataset.source_kind NOT IN ('Raw','Native') OR dataset.period_id IS DISTINCT FROM c.period_id OR
         dataset.book_id IS DISTINCT FROM c.book_id OR dataset.basis IS DISTINCT FROM c.basis OR
         dataset.legal_entity_key<>c.legal_entity_key OR dataset.currency<>c.currency OR
         accepted.accepted_by_user_id=dataset.imported_by_user_id OR
         accepted.source_identity_hash IS DISTINCT FROM
           (CASE WHEN dataset.source_kind='Native' OR dataset.normalized_dataset_digest='' THEN dataset.sha256_hex ELSE dataset.normalized_dataset_digest END) OR
         NOT audit_schedule_source_matches(to_jsonb(dataset),m#>'{TrialBalance,Dataset}') THEN
        RAISE EXCEPTION 'Schedule TB context, independent acceptance or sealed identity changed.' USING ERRCODE='23514';
      END IF;
      SELECT d.id INTO current_decision FROM source_acceptance_decisions d JOIN trial_balance_datasets t ON t.id=d.trial_balance_dataset_id
        WHERE d.firm_id=c.firm_id AND d.client_id=c.client_id AND d.engagement_id=c.engagement_id AND
          d.source_kind='TB' AND d.decision='ACCEPTED' AND t.firm_id=c.firm_id AND t.client_id=c.client_id AND t.engagement_id=c.engagement_id AND
          t.period_id=c.period_id AND t.book_id IS NOT DISTINCT FROM c.book_id AND t.basis IS NOT DISTINCT FROM c.basis AND
          t.currency=c.currency AND t.legal_entity_key=c.legal_entity_key ORDER BY d.created_at DESC,d.id DESC LIMIT 1;
      IF current_decision IS DISTINCT FROM c.accepted_source_decision_id THEN
        RAISE EXCEPTION 'The selected exact-context accepted TB changed.' USING ERRCODE='23514';
      END IF;
      SELECT * INTO period_row FROM client_reporting_periods WHERE id=c.period_id AND firm_id=c.firm_id AND client_id=c.client_id FOR SHARE;
      IF NOT FOUND OR period_row.status<>'ACTIVE' OR period_row.revision<>c.period_revision OR period_row.end_date<>c.source_date OR
         period_row.currency<>c.currency OR (c.basis IS NOT NULL AND period_row.basis<>c.basis) OR
         request_row.period_start<>period_row.start_date::text OR request_row.period_end<>period_row.end_date::text OR
         NOT audit_schedule_source_matches(to_jsonb(period_row),m#>'{TrialBalance,Period}') THEN
        RAISE EXCEPTION 'Schedule closing date or reporting period changed.' USING ERRCODE='23514';
      END IF;
      IF c.book_id IS NOT NULL THEN
        SELECT * INTO book_row FROM client_reporting_books WHERE id=c.book_id AND firm_id=c.firm_id AND client_id=c.client_id FOR SHARE;
        IF NOT FOUND OR book_row.period_id<>c.period_id OR book_row.status<>'ACTIVE' OR book_row.currency<>c.currency OR
           (c.basis IS NOT NULL AND book_row.basis<>c.basis) OR NOT audit_schedule_source_matches(to_jsonb(book_row),m#>'{TrialBalance,Book}') THEN
          RAISE EXCEPTION 'The exact schedule reporting book changed.' USING ERRCODE='23514';
        END IF;
      ELSIF m#>'{TrialBalance,Book}' IS DISTINCT FROM 'null'::jsonb THEN
        RAISE EXCEPTION 'An absent reporting book cannot be invented.' USING ERRCODE='23514';
      END IF;
      IF (m#>>'{TrialBalance,InputGeneration}')::bigint IS DISTINCT FROM c.input_generation THEN
        RAISE EXCEPTION 'Captured source generation differs.' USING ERRCODE='23514';
      END IF;
      IF dataset.source_kind='Native' THEN
        SELECT * INTO native FROM native_trial_balance_sources WHERE dataset_id=dataset.id AND firm_id=c.firm_id
          AND client_id=c.client_id AND engagement_id=c.engagement_id;
        IF NOT FOUND OR native.source_identity_hash<>accepted.source_identity_hash OR
           (native.manifest_json::jsonb->>'ActivityOnly')::boolean IS DISTINCT FROM false OR
           (native.manifest_json::jsonb->>'AsOfDate')::date IS DISTINCT FROM c.source_date OR
           (native.manifest_json::jsonb->>'PeriodEnd')::date IS DISTINCT FROM c.source_date THEN
          RAISE EXCEPTION 'Native schedule TB requires complete retained period-end provenance.' USING ERRCODE='23514';
        END IF;
      END IF;
    END; $fn$;

    CREATE FUNCTION audit_schedule_source_check_capture(target uuid, require_current boolean) RETURNS void
      LANGUAGE plpgsql AS $fn$
    DECLARE c audit_schedule_source_captures%ROWTYPE; s audit_schedules%ROWTYPE;
      m jsonb; req jsonb; receipt jsonb; normalized jsonb; rows_json jsonb; originals jsonb;
      selected jsonb; controls jsonb; ties jsonb; row_json jsonb; tie jsonb; code text;
      originals_by_id jsonb; original_row jsonb; selected_by_code jsonb; ties_by_code jsonb;
      listing_by_code jsonb; tb_by_code jsonb;
      db_row audit_schedule_rows%ROWTYPE; tb_row trial_balance_rows%ROWTYPE;
      listing_count bigint; tb_count bigint; row_count bigint; listing_sum numeric; tb_sum numeric;
      total numeric; closing numeric; eligible boolean := true; base_status text; review_row audit_schedule_source_reviews%ROWTYPE;
    BEGIN
      SELECT * INTO c FROM audit_schedule_source_captures WHERE schedule_id=target;
      IF NOT FOUND THEN RETURN; END IF;
      SELECT * INTO s FROM audit_schedules WHERE id=target;
      IF NOT FOUND THEN RAISE EXCEPTION 'Missing captured core schedule.' USING ERRCODE='23514'; END IF;
      m:=c.manifest_json::jsonb; req:=c.request_json::jsonb; receipt:=c.receipt_json::jsonb;
      IF NOT COALESCE(m @> jsonb_build_object('Version','audit-schedule-source-capture.v1','FirmId',c.firm_id,
           'ClientId',c.client_id,'EngagementId',c.engagement_id,'ScheduleType','RECEIVABLES_LEAD',
           'SourceDate',c.source_date,'ProfileVersion',c.profile_version,'ControlBasis','TB_CLOSING_SIGNED_NET'),false) OR
         c.firm_id<>s.firm_id OR c.client_id<>s.client_id OR c.engagement_id<>s.engagement_id OR c.actor_user_id<>s.created_by_user_id OR
         c.profile_version<>'audit-client-listing-csv.v1' OR s.schedule_type<>'RECEIVABLES_LEAD' OR
         s.entity_identifier<>c.legal_entity_key OR s.currency<>c.currency OR s.input_generation<>c.input_generation OR
         s.as_of_date IS NULL OR s.as_of_date IS DISTINCT FROM (c.source_date::text||'T00:00:00Z')::timestamptz OR
         s.period_start IS DISTINCT FROM (m#>>'{TrialBalance,Period,StartDate}')::date OR
         s.period_end IS DISTINCT FROM c.source_date OR s.source_import_batch_id IS NOT NULL OR
         s.source_receipt_reference IS DISTINCT FROM 'audit-source/'||c.request_id::text OR
         s.source_hash IS DISTINCT FROM m->>'RawFileSha256' OR
         s.accepted_source_decision_id IS DISTINCT FROM c.accepted_source_decision_id OR
         s.accepted_source_hash IS DISTINCT FROM m#>>'{TrialBalance,Decision,SourceIdentityHash}' OR
         s.sign_convention<>'debits positive; credits negative' THEN
        RAISE EXCEPTION 'Captured schedule source/header binding failed.' USING ERRCODE='23514';
      END IF;
      IF NOT COALESCE(req @> jsonb_build_object('Version','audit-schedule-source-command.v1','Action','CAPTURE',
           'FirmId',c.firm_id,'ClientId',c.client_id,'EngagementId',c.engagement_id,'ActorUserId',c.actor_user_id,
           'ActorEpoch',c.actor_epoch,'RequestId',c.request_id,'ExpectedManifestHash',c.manifest_hash,
           'ExpectedInputGeneration',c.input_generation,'ExpectedPeriodRevision',c.period_revision),false) OR
         NOT COALESCE(req->'Fields' @> jsonb_build_object('PbcAcceptanceReceiptId',c.pbc_acceptance_receipt_id,
           'AcceptedSourceDecisionId',c.accepted_source_decision_id,'SourceDate',c.source_date),false) OR
         req#>'{Fields,Columns}' IS DISTINCT FROM m->'Columns' OR
         req#>'{Fields,SelectedControlAccountCodes}' IS DISTINCT FROM m->'SelectedControlAccountCodes' THEN
        RAISE EXCEPTION 'Capture command must retain its exact actor and original source intent.' USING ERRCODE='23514';
      END IF;
      IF NOT COALESCE((m#>'{Pbc,Receipt}') ?& ARRAY['Id','FirmId','ClientId','EngagementId','PbcRequestId',
           'AcceptedRequestRevision','SourceRequestRevision','ReviewerUserId','AcceptedAt','PbcUploadIntentId',
           'UploadIntentRevision','UploaderUserId','FileName','ByteCount','ContentSha256Hex','TransferOperationId',
           'ProviderIdentity','ProviderReceiptDigest','ProviderRegisteredAt','SourceDigest'],false) OR
         NOT COALESCE((m#>'{Pbc,Request}') ?& ARRAY['Id','EntityScope','PeriodStart','PeriodEnd','Area',
           'RequestedFormat','ReviewerUserId','AcceptedByUserId','AcceptedAt'],false) OR
         NOT COALESCE((m#>'{Pbc,Upload}') ?& ARRAY['Id','RequestRevision','Revision','State','UploaderUserId','FileName',
           'ContentType','DeclaredByteCount','DeclaredSha256Hex','ReceivedByteCount','FinalSha256Hex','CreatedAt',
           'CompletedAt','TransferOperationId','ProviderReceiptDigest','ProviderRegisteredAt'],false) OR
         NOT COALESCE((m#>'{Pbc,Transfer}') ?& ARRAY['Id','OperationKind','TargetId','ExpectedRevision',
           'Status','ResultIdentity','ResultDigest','CompletedAt'],false) OR
         NOT COALESCE((m#>'{TrialBalance,Decision}') ?& ARRAY['Id','FirmId','ClientId','EngagementId','SourceKind',
           'TrialBalanceDatasetId','ImportBatchId','SourceIdentityHash','Decision','EvidenceReference','AcceptedByUserId','CreatedAt'],false) OR
         NOT COALESCE((m#>'{TrialBalance,Dataset}') ?& ARRAY['Id','PeriodId','BookId','Basis','ImportBatchId','SourceKind',
           'Revision','LegalEntityKey','Currency','RawFileSha256Hex','NormalizedDatasetDigest','Sha256Hex','ImportProfileVersion',
           'SourceLayout','Balanced','ValidationStatus','ImportState','ControlTotal','ImportedAt','ImportedByUserId'],false) OR
         NOT COALESCE((m#>'{TrialBalance,Period}') ?& ARRAY['Id','PeriodCode','StartDate','EndDate','Basis','Currency','Status','Revision'],false) OR
         jsonb_typeof(m->'Columns') IS DISTINCT FROM 'object' OR
         NOT COALESCE(m->'Columns' ?& ARRAY['AccountCode','Description','SignedAmount','Currency'],false) OR
         jsonb_typeof(m->'Headers') IS DISTINCT FROM 'array' THEN
        RAISE EXCEPTION 'Complete actual source witnesses and parser mapping are required.' USING ERRCODE='23514';
      END IF;
      IF c.book_id IS NOT NULL AND NOT COALESCE((m#>'{TrialBalance,Book}') ?& ARRAY['Id','Code','Basis','Currency','Status','Revision'],false) THEN
        RAISE EXCEPTION 'The explicit reporting book witness is incomplete.' USING ERRCODE='23514';
      END IF;
      IF jsonb_array_length(m->'Headers') NOT BETWEEN 1 AND 256 THEN
        RAISE EXCEPTION 'Parser header witness exceeds the bounded profile.' USING ERRCODE='23514';
      END IF;
      IF jsonb_typeof(m->'NormalizedRowsJson') IS DISTINCT FROM 'string' OR
         jsonb_typeof(m->'OriginalRows') IS DISTINCT FROM 'array' OR
         NOT COALESCE((m->>'NormalizedRowsSha256') ~ '^[0-9a-f]{64}$',false) OR
         m->>'NormalizedRowsSha256' IS DISTINCT FROM encode(sha256(convert_to(m->>'NormalizedRowsJson','UTF8')),'hex') THEN
        RAISE EXCEPTION 'Complete original and exact normalized rows are required.' USING ERRCODE='23514';
      END IF;
      normalized:=(m->>'NormalizedRowsJson')::jsonb; rows_json:=normalized->'Rows'; originals:=m->'OriginalRows';
      IF NOT COALESCE(normalized @> jsonb_build_object('ProfileVersion',c.profile_version,'Currency',c.currency),false) OR
         jsonb_typeof(rows_json) IS DISTINCT FROM 'array' THEN
        RAISE EXCEPTION 'Malformed normalized row witness.' USING ERRCODE='23514';
      END IF;
      row_count:=jsonb_array_length(rows_json);
      IF row_count NOT BETWEEN 1 AND 20000 OR jsonb_array_length(originals)<>row_count OR s.row_count<>row_count OR
         (m->>'RowCount')::bigint IS DISTINCT FROM row_count OR
         (SELECT count(*) FROM audit_schedule_rows WHERE firm_id=c.firm_id AND schedule_id=target)<>row_count OR
         (SELECT count(DISTINCT x->>'StableRowId') FROM jsonb_array_elements(rows_json) x)<>row_count OR
         (SELECT count(DISTINCT x->>'StableRowId') FROM jsonb_array_elements(originals) x)<>row_count THEN
        RAISE EXCEPTION 'No normalized/original/core schedule rows may be omitted or duplicated.' USING ERRCODE='23514';
      END IF;
      SELECT jsonb_object_agg(o->>'StableRowId',o) INTO originals_by_id
        FROM jsonb_array_elements(originals) o;
      total:=0;
      FOR row_json IN SELECT value FROM jsonb_array_elements(rows_json) LOOP
        SELECT * INTO db_row FROM audit_schedule_rows WHERE firm_id=c.firm_id AND
          schedule_id=target AND stable_row_id=row_json->>'StableRowId';
        original_row:=originals_by_id->(row_json->>'StableRowId');
        IF NOT FOUND OR NOT COALESCE(row_json ?& ARRAY['StableRowId','SourceLineNumber','AccountCode','Description',
             'SignedAmount','Currency','TransactionDate','PostingDate','DeliveryDate','ServiceDate'],false) OR db_row.firm_id<>c.firm_id OR db_row.client_id<>c.client_id OR db_row.engagement_id<>c.engagement_id OR
           db_row.created_at<>s.created_at OR db_row.currency<>c.currency OR
           NOT audit_schedule_source_matches(to_jsonb(db_row),row_json) OR
           db_row.signed_amount IS DISTINCT FROM audit_schedule_source_money(row_json->>'SignedAmount') OR
           original_row IS NULL OR
           (original_row->>'SourceLineNumber')::integer IS DISTINCT FROM db_row.source_line_number OR
           jsonb_typeof(original_row->'OriginalValuesJson') IS DISTINCT FROM 'string' OR
           original_row->>'OriginalValuesJson' IS DISTINCT FROM db_row.original_values_json OR
           jsonb_typeof((original_row->>'OriginalValuesJson')::jsonb) IS DISTINCT FROM 'object' THEN
          RAISE EXCEPTION 'Core schedule rows must reproduce exact normalized values and original cell JSON.' USING ERRCODE='23514';
        END IF;
        total:=total+db_row.signed_amount;
      END LOOP;
      selected:=m->'SelectedControlAccountCodes'; controls:=m->'ControlRows'; ties:=m->'AccountTieOuts';
      IF jsonb_typeof(selected) IS DISTINCT FROM 'array' OR jsonb_typeof(controls) IS DISTINCT FROM 'array' OR
         jsonb_typeof(ties) IS DISTINCT FROM 'array' THEN
        RAISE EXCEPTION 'Explicit account scope and complete TB witnesses are required.' USING ERRCODE='23514';
      END IF;
      IF jsonb_array_length(selected) NOT BETWEEN 1 AND 20000 OR jsonb_array_length(controls) NOT BETWEEN 1 AND 20000 OR
         jsonb_array_length(ties)<>jsonb_array_length(selected) OR
         (SELECT count(DISTINCT x#>>'{}') FROM jsonb_array_elements(selected) x)<>jsonb_array_length(selected) OR
         EXISTS (SELECT 1 FROM jsonb_array_elements(selected) x WHERE jsonb_typeof(x)<>'string' OR
           length(trim(x#>>'{}')) NOT BETWEEN 1 AND 100 OR trim(x#>>'{}')<>x#>>'{}') THEN
        RAISE EXCEPTION 'Complete distinct selected-account coverage is required.' USING ERRCODE='23514';
      END IF;
      -- Build lookup objects only AFTER duplicate/null-key validation. Otherwise object
      -- aggregation would silently overwrite duplicate evidence. Membership is indexed too.
      SELECT jsonb_object_agg(value,true) INTO selected_by_code FROM jsonb_array_elements_text(selected);
      IF (SELECT count(DISTINCT x->>'Id') FROM jsonb_array_elements(controls) x)<>jsonb_array_length(controls) OR
         (SELECT count(DISTINCT x->>'AccountCode') FROM jsonb_array_elements(ties) x)<>jsonb_array_length(ties) OR
         EXISTS (SELECT 1 FROM audit_schedule_rows r WHERE r.firm_id=c.firm_id AND r.schedule_id=target AND NOT selected_by_code ? r.account_code) OR
         (SELECT count(*) FROM trial_balance_rows r WHERE r.dataset_id=c.trial_balance_dataset_id AND selected_by_code ? r.account_code)<>jsonb_array_length(controls) THEN
        RAISE EXCEPTION 'Complete distinct selected-account coverage is required.' USING ERRCODE='23514';
      END IF;
      closing:=0;
      FOR row_json IN SELECT value FROM jsonb_array_elements(controls) LOOP
        SELECT * INTO tb_row FROM trial_balance_rows WHERE id=(row_json->>'Id')::uuid AND dataset_id=c.trial_balance_dataset_id;
        IF NOT FOUND OR NOT COALESCE(row_json ?& ARRAY['Id','DatasetId','AccountCode','AccountName','Amount',
             'SourceDebit','SourceCredit','Currency','Entity','MappingCode'],false) OR NOT selected_by_code ? tb_row.account_code OR tb_row.currency<>c.currency OR tb_row.entity<>c.legal_entity_key OR
           NOT audit_schedule_source_matches(to_jsonb(tb_row),row_json) OR
           NOT COALESCE(row_json @> jsonb_build_object('Id',tb_row.id,'DatasetId',c.trial_balance_dataset_id,
             'AccountCode',tb_row.account_code,'Currency',c.currency,'Entity',c.legal_entity_key),false) OR
           audit_schedule_source_money(row_json->>'Amount') IS DISTINCT FROM tb_row.amount THEN
          RAISE EXCEPTION 'TB control rows must be actual complete selected source witnesses.' USING ERRCODE='23514';
        END IF;
        closing:=closing+tb_row.amount;
      END LOOP;
      SELECT jsonb_object_agg(t->>'AccountCode',t) INTO ties_by_code FROM jsonb_array_elements(ties) t;
      -- Aggregate each complete population once, retaining exact sums and explicit counts.
      -- An absent listing remains count=0 (ineligible); an absent TB account is rejected below.
      SELECT COALESCE(jsonb_object_agg(g.account_code,jsonb_build_object('Count',g.row_count,'Amount',g.amount)),'{}'::jsonb)
        INTO listing_by_code FROM (
          SELECT account_code,count(*) AS row_count,sum(signed_amount) AS amount FROM audit_schedule_rows
          WHERE firm_id=c.firm_id AND schedule_id=target GROUP BY account_code
        ) g;
      SELECT COALESCE(jsonb_object_agg(g.account_code,jsonb_build_object('Count',g.row_count,'Amount',g.amount)),'{}'::jsonb)
        INTO tb_by_code FROM (
          SELECT account_code,count(*) AS row_count,sum(amount) AS amount FROM trial_balance_rows
          WHERE dataset_id=c.trial_balance_dataset_id AND selected_by_code ? account_code GROUP BY account_code
        ) g;
      FOR code IN SELECT value FROM jsonb_array_elements_text(selected) LOOP
        listing_count:=COALESCE((listing_by_code->code->>'Count')::bigint,0);
        listing_sum:=COALESCE((listing_by_code->code->>'Amount')::numeric,0);
        tb_count:=COALESCE((tb_by_code->code->>'Count')::bigint,0);
        tb_sum:=COALESCE((tb_by_code->code->>'Amount')::numeric,0);
        tie:=ties_by_code->code;
        IF tie IS NULL OR tb_count=0 OR (tie->>'ListingRowCount')::bigint IS DISTINCT FROM listing_count OR
           (tie->>'TrialBalanceRowCount')::bigint IS DISTINCT FROM tb_count OR
           audit_schedule_source_money(tie->>'ListingAmount')<>listing_sum OR
           audit_schedule_source_money(tie->>'TrialBalanceAmount')<>tb_sum OR
           audit_schedule_source_money(tie->>'Difference')<>listing_sum-tb_sum THEN
          RAISE EXCEPTION 'Every selected account must reproduce its own exact listing/TB difference.' USING ERRCODE='23514';
        END IF;
        eligible:=eligible AND listing_count>0 AND listing_sum=tb_sum;
      END LOOP;
      IF total IS DISTINCT FROM audit_schedule_source_money(m->>'SignedTotal') OR
         closing IS DISTINCT FROM audit_schedule_source_money(m->>'ClosingControl') OR
         total-closing IS DISTINCT FROM audit_schedule_source_money(m->>'Residual') OR
         s.signed_control_total<>total OR s.gl_control_total<>closing OR s.residual<>total-closing THEN
        RAISE EXCEPTION 'Captured complete signed controls differ from the actual population.' USING ERRCODE='23514';
      END IF;
      base_status:=CASE WHEN eligible THEN 'RECONCILED' ELSE 'UNRECONCILED' END;
      IF NOT COALESCE(receipt @> jsonb_build_object('Version','audit-schedule-source-receipt.v1','Action','CAPTURE',
           'ScheduleId',c.schedule_id,'ReviewId',NULL::uuid,'FirmId',c.firm_id,'ClientId',c.client_id,'EngagementId',c.engagement_id,
           'ActorUserId',c.actor_user_id,'ActorEpoch',c.actor_epoch::text,'RequestId',c.request_id,'RequestHash',c.request_hash,
           'ManifestHash',c.manifest_hash,'InputGeneration',c.input_generation::text,'RowCount',row_count::text,
           'SignedTotal',m->>'SignedTotal','ClosingControl',m->>'ClosingControl','Residual',m->>'Residual','Status',base_status),false) OR
         (receipt->>'CreatedAt')::timestamptz IS DISTINCT FROM c.created_at THEN
        RAISE EXCEPTION 'Capture original receipt does not bind the actual command outcome.' USING ERRCODE='23514';
      END IF;
      SELECT * INTO review_row FROM audit_schedule_source_reviews WHERE schedule_id=target;
      IF FOUND THEN
        IF s.status IS DISTINCT FROM (CASE WHEN review_row.action='APPROVE' THEN 'APPROVED' ELSE 'UNRECONCILED' END) OR
           s.completeness_decision IS DISTINCT FROM review_row.reason THEN
          RAISE EXCEPTION 'Typed core review projection requires its exact retained decision.' USING ERRCODE='23514';
        END IF;
      ELSIF s.status<>base_status OR s.completeness_decision<>'' THEN
        RAISE EXCEPTION 'A typed schedule cannot acquire approval or completeness without a retained review.' USING ERRCODE='23514';
      END IF;
      IF require_current THEN PERFORM audit_schedule_source_current(c); END IF;
    END; $fn$;

    CREATE FUNCTION audit_schedule_source_check_review() RETURNS trigger LANGUAGE plpgsql AS $fn$
    DECLARE c audit_schedule_source_captures%ROWTYPE; s audit_schedules%ROWTYPE;
      req jsonb:=NEW.request_json::jsonb; receipt jsonb:=NEW.receipt_json::jsonb; m jsonb;
      generation bigint; projected text;
    BEGIN
      SELECT * INTO c FROM audit_schedule_source_captures WHERE schedule_id=NEW.schedule_id AND firm_id=NEW.firm_id
        AND client_id=NEW.client_id AND engagement_id=NEW.engagement_id;
      IF NOT FOUND OR NEW.capture_hash<>c.manifest_hash OR NEW.actor_user_id=c.actor_user_id OR
         (SELECT count(*) FROM audit_schedule_source_reviews WHERE schedule_id=NEW.schedule_id)<>1 THEN
        RAISE EXCEPTION 'One independent decision must bind the exact original capture.' USING ERRCODE='23514';
      END IF;
      PERFORM id FROM firm_safety_states WHERE id=NEW.firm_id FOR SHARE;
      IF NOT FOUND THEN RAISE EXCEPTION 'Review firm scope is unavailable.' USING ERRCODE='23514'; END IF;
      SELECT input_generation INTO generation FROM client_safety_states WHERE firm_id=NEW.firm_id AND id=NEW.client_id FOR UPDATE;
      IF NOT FOUND OR generation<>NEW.input_generation THEN
        RAISE EXCEPTION 'Review generation is not current.' USING ERRCODE='23514';
      END IF;
      PERFORM id FROM engagements WHERE firm_id=NEW.firm_id AND id=NEW.engagement_id AND practice_client_id=NEW.client_id FOR UPDATE;
      IF NOT FOUND THEN RAISE EXCEPTION 'Review scope is unavailable.' USING ERRCODE='23514'; END IF;
      SELECT * INTO s FROM audit_schedules WHERE id=NEW.schedule_id FOR UPDATE;
      IF NOT FOUND OR s.created_by_user_id=NEW.actor_user_id THEN
        RAISE EXCEPTION 'The original schedule maker cannot review it.' USING ERRCODE='23514';
      END IF;
      PERFORM id FROM users WHERE id=NEW.actor_user_id AND firm_id=NEW.firm_id AND NOT disabled AND session_epoch=NEW.actor_epoch;
      IF NOT FOUND THEN RAISE EXCEPTION 'Review actor session is not current.' USING ERRCODE='23514'; END IF;
      PERFORM audit_schedule_source_check_capture(NEW.schedule_id,false);
      m:=c.manifest_json::jsonb;
      IF NEW.action='APPROVE' THEN
        PERFORM audit_schedule_source_current(c);
        IF EXISTS (SELECT 1 FROM jsonb_array_elements(m->'AccountTieOuts') t
          WHERE audit_schedule_source_money(t->>'Difference')<>0 OR (t->>'ListingRowCount')::bigint<1) THEN
          RAISE EXCEPTION 'Each selected account needs complete reconciled listing coverage before approval.' USING ERRCODE='23514';
        END IF;
        projected:='APPROVED';
      ELSE
        -- Refusal retains the exact old capture; it never pretends stale sources are fresh.
        projected:='UNRECONCILED';
      END IF;
      IF NOT COALESCE(req @> jsonb_build_object('Version','audit-schedule-source-command.v1',
           'FirmId',NEW.firm_id,'ClientId',NEW.client_id,'EngagementId',NEW.engagement_id,'ActorUserId',NEW.actor_user_id,
           'ActorEpoch',NEW.actor_epoch,'RequestId',NEW.request_id,'ScheduleId',NEW.schedule_id,
           'ExpectedCaptureHash',c.manifest_hash,'ExpectedInputGeneration',NEW.input_generation),false) OR
         -- The workspace verifies the frozen current preview recipe; SQL binds the retained request.
         NOT COALESCE((req->>'ExpectedReviewBasis') ~ '^[0-9a-f]{64}$',false) OR
         NOT COALESCE(req->'Fields' @> jsonb_build_object('Action',NEW.action,'Reason',NEW.reason,
           'EvidenceReference',NEW.evidence_reference),false) OR
         NOT COALESCE(receipt @> jsonb_build_object('Version','audit-schedule-source-receipt.v1','Action',NEW.action,
           'ScheduleId',NEW.schedule_id,'ReviewId',NEW.id,'FirmId',NEW.firm_id,'ClientId',NEW.client_id,'EngagementId',NEW.engagement_id,
           'ActorUserId',NEW.actor_user_id,'ActorEpoch',NEW.actor_epoch::text,'RequestId',NEW.request_id,'RequestHash',NEW.request_hash,
           'ManifestHash',c.manifest_hash,'InputGeneration',NEW.input_generation::text,'RowCount',s.row_count::text,
           'SignedTotal',m->>'SignedTotal','ClosingControl',m->>'ClosingControl','Residual',m->>'Residual','Status',projected),false) OR
         (receipt->>'CreatedAt')::timestamptz IS DISTINCT FROM NEW.created_at OR
         s.status<>projected OR s.completeness_decision<>NEW.reason THEN
        RAISE EXCEPTION 'Review request, original receipt and core projection must agree exactly.' USING ERRCODE='23514';
      END IF;
      RETURN NEW;
    END; $fn$;

    CREATE FUNCTION audit_schedule_source_capture_insert() RETURNS trigger LANGUAGE plpgsql AS $fn$
    BEGIN
      PERFORM id FROM users WHERE id=NEW.actor_user_id AND firm_id=NEW.firm_id AND NOT disabled AND session_epoch=NEW.actor_epoch;
      IF NOT FOUND THEN RAISE EXCEPTION 'Capture actor session is not current.' USING ERRCODE='23514'; END IF;
      PERFORM audit_schedule_source_check_capture(NEW.schedule_id,true);
      RETURN NEW;
    END; $fn$;
    CREATE CONSTRAINT TRIGGER trg_schedule_source_capture_binding AFTER INSERT ON audit_schedule_source_captures
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION audit_schedule_source_capture_insert();
    CREATE CONSTRAINT TRIGGER trg_schedule_source_review_binding AFTER INSERT ON audit_schedule_source_reviews
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION audit_schedule_source_check_review();

    CREATE FUNCTION protect_audit_schedule_source_evidence() RETURNS trigger LANGUAGE plpgsql AS $fn$
    BEGIN
      RAISE EXCEPTION 'Schedule source captures and review receipts are append-only.' USING ERRCODE='55000';
    END; $fn$;
    CREATE TRIGGER trg_schedule_source_capture_immutable BEFORE UPDATE OR DELETE ON audit_schedule_source_captures
      FOR EACH ROW EXECUTE FUNCTION protect_audit_schedule_source_evidence();
    CREATE TRIGGER trg_schedule_source_capture_no_truncate BEFORE TRUNCATE ON audit_schedule_source_captures
      FOR EACH STATEMENT EXECUTE FUNCTION protect_audit_schedule_source_evidence();
    CREATE TRIGGER trg_schedule_source_review_immutable BEFORE UPDATE OR DELETE ON audit_schedule_source_reviews
      FOR EACH ROW EXECUTE FUNCTION protect_audit_schedule_source_evidence();
    CREATE TRIGGER trg_schedule_source_review_no_truncate BEFORE TRUNCATE ON audit_schedule_source_reviews
      FOR EACH STATEMENT EXECUTE FUNCTION protect_audit_schedule_source_evidence();

    CREATE FUNCTION protect_captured_audit_schedule() RETURNS trigger LANGUAGE plpgsql AS $fn$
    BEGIN
      IF EXISTS (SELECT 1 FROM audit_schedule_source_captures WHERE schedule_id=OLD.id) THEN
        IF TG_OP='DELETE' THEN
          RAISE EXCEPTION 'A captured schedule cannot be deleted.' USING ERRCODE='55000';
        END IF;
        IF (to_jsonb(NEW)-ARRAY['status','completeness_decision']) IS DISTINCT FROM
           (to_jsonb(OLD)-ARRAY['status','completeness_decision']) THEN
          RAISE EXCEPTION 'Captured schedule source fields are immutable.' USING ERRCODE='55000';
        END IF;
      END IF;
      IF TG_OP='DELETE' THEN RETURN OLD; END IF;
      RETURN NEW;
    END; $fn$;
    CREATE TRIGGER trg_captured_schedule_immutable BEFORE UPDATE OR DELETE ON audit_schedules
      FOR EACH ROW EXECUTE FUNCTION protect_captured_audit_schedule();

    CREATE FUNCTION check_captured_audit_schedule_projection() RETURNS trigger LANGUAGE plpgsql AS $fn$
    BEGIN
      PERFORM audit_schedule_source_check_capture(NEW.id,false);
      RETURN NEW;
    END; $fn$;
    CREATE CONSTRAINT TRIGGER trg_captured_schedule_projection AFTER INSERT OR UPDATE ON audit_schedules
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_captured_audit_schedule_projection();

    CREATE FUNCTION protect_captured_audit_schedule_rows() RETURNS trigger LANGUAGE plpgsql AS $fn$
    BEGIN
      IF TG_OP IN ('UPDATE','DELETE') AND EXISTS (SELECT 1 FROM audit_schedule_source_captures WHERE schedule_id=OLD.schedule_id) THEN
        RAISE EXCEPTION 'Captured original schedule rows are immutable.' USING ERRCODE='55000';
      END IF;
      IF TG_OP IN ('INSERT','UPDATE') AND EXISTS (SELECT 1 FROM audit_schedule_source_captures
        WHERE schedule_id=NEW.schedule_id) THEN
        RAISE EXCEPTION 'A retained capture cannot acquire additional or moved source rows.' USING ERRCODE='55000';
      END IF;
      -- Publish complete core rows before inserting their immutable capture.
      IF TG_OP='DELETE' THEN RETURN OLD; END IF;
      RETURN NEW;
    END; $fn$;
    CREATE TRIGGER trg_captured_schedule_rows_immutable BEFORE INSERT OR UPDATE OR DELETE ON audit_schedule_rows
      FOR EACH ROW EXECUTE FUNCTION protect_captured_audit_schedule_rows();

    CREATE FUNCTION protect_captured_audit_schedule_truncate() RETURNS trigger LANGUAGE plpgsql AS $fn$
    BEGIN
      IF EXISTS (SELECT 1 FROM audit_schedule_source_captures) OR EXISTS (SELECT 1 FROM audit_schedule_source_reviews) THEN
        RAISE EXCEPTION 'Retained typed schedule evidence cannot be truncated.' USING ERRCODE='55000';
      END IF;
      RETURN NULL;
    END; $fn$;
    CREATE TRIGGER trg_captured_schedules_no_truncate BEFORE TRUNCATE ON audit_schedules
      FOR EACH STATEMENT EXECUTE FUNCTION protect_captured_audit_schedule_truncate();
    CREATE TRIGGER trg_captured_schedule_rows_no_truncate BEFORE TRUNCATE ON audit_schedule_rows
      FOR EACH STATEMENT EXECUTE FUNCTION protect_captured_audit_schedule_truncate();
    """;

  // Parent Down must refuse any retained capture/review BEFORE invoking this helper.
  internal const string Uninstall = """
    DROP TRIGGER trg_captured_schedule_rows_no_truncate ON audit_schedule_rows;
    DROP TRIGGER trg_captured_schedules_no_truncate ON audit_schedules;
    DROP TRIGGER trg_captured_schedule_rows_immutable ON audit_schedule_rows;
    DROP TRIGGER trg_captured_schedule_projection ON audit_schedules;
    DROP TRIGGER trg_captured_schedule_immutable ON audit_schedules;
    DROP TRIGGER trg_schedule_source_review_no_truncate ON audit_schedule_source_reviews;
    DROP TRIGGER trg_schedule_source_review_immutable ON audit_schedule_source_reviews;
    DROP TRIGGER trg_schedule_source_capture_no_truncate ON audit_schedule_source_captures;
    DROP TRIGGER trg_schedule_source_capture_immutable ON audit_schedule_source_captures;
    DROP TRIGGER trg_schedule_source_review_binding ON audit_schedule_source_reviews;
    DROP TRIGGER trg_schedule_source_capture_binding ON audit_schedule_source_captures;
    DROP FUNCTION protect_captured_audit_schedule_truncate();
    DROP FUNCTION protect_captured_audit_schedule_rows();
    DROP FUNCTION check_captured_audit_schedule_projection();
    DROP FUNCTION protect_captured_audit_schedule();
    DROP FUNCTION protect_audit_schedule_source_evidence();
    DROP FUNCTION audit_schedule_source_capture_insert();
    DROP FUNCTION audit_schedule_source_check_review();
    DROP FUNCTION audit_schedule_source_check_capture(uuid,boolean);
    DROP FUNCTION audit_schedule_source_current(audit_schedule_source_captures);
    DROP FUNCTION audit_schedule_source_matches(jsonb,jsonb);
    DROP FUNCTION audit_schedule_source_money(text);
    """;
}
