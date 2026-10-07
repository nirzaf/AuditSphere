namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

/// <summary>Database proof for invoice-only native ledger origin and append-only review history.</summary>
internal static class ClientSalesInvoiceWorkflowSql
{
  internal const string Up = """

    -- Immutable documentary source and client-owned ledger proof. The historical proof deliberately
    -- reads the database-captured submitted snapshot, not a mutable returned/reworked journal header.
    CREATE FUNCTION client_sales_round(value numeric, places integer, midpoint text) RETURNS numeric
      LANGUAGE plpgsql IMMUTABLE STRICT AS $fn$
    DECLARE scaled numeric; whole numeric; factor numeric;
    BEGIN
      IF places NOT BETWEEN 0 AND 6 OR midpoint NOT IN ('TO_EVEN','AWAY_FROM_ZERO') THEN
        RAISE EXCEPTION 'Unsupported invoice rounding declaration.' USING ERRCODE='23514';
      END IF;
      factor:=power(10::numeric,places); scaled:=value*factor; whole:=trunc(scaled);
      IF midpoint='TO_EVEN' AND abs(scaled-whole)=0.5 AND mod(abs(whole),2)=0 THEN RETURN whole/factor; END IF;
      RETURN round(value,places);
    END; $fn$;

    CREATE FUNCTION assert_client_sales_submission(submission_key uuid, validate_current boolean DEFAULT false,
      require_original_mandate boolean DEFAULT false) RETURNS void LANGUAGE plpgsql AS $fn$
    DECLARE s client_sales_invoice_submissions%ROWTYPE; d client_sales_invoice_drafts%ROWTYPE;
      rc client_account_role_configurations%ROWTYPE; rd client_account_role_decisions%ROWTYPE;
      p client_accounting_profiles%ROWTYPE; mandate acceptance_decisions%ROWTYPE;
      m jsonb; body jsonb; captured jsonb; header jsonb; item jsonb; amount numeric;
      expected_lines jsonb; actual_lines jsonb; n integer; decimals integer; midpoint text;
    BEGIN
      SELECT * INTO s FROM client_sales_invoice_submissions WHERE id=submission_key;
      IF NOT FOUND THEN RAISE EXCEPTION 'An exact invoice submission is required.' USING ERRCODE='23514'; END IF;
      SELECT * INTO d FROM client_sales_invoice_drafts WHERE firm_id=s.firm_id AND client_id=s.client_id
        AND invoice_id=s.invoice_id AND id=s.draft_id AND revision=s.draft_revision;
      IF NOT FOUND OR d.created_by_user_id<>s.created_by_user_id
        OR encode(sha256(convert_to(d.snapshot_json,'UTF8')),'hex')<>d.snapshot_hash
        OR encode(sha256(convert_to(s.manifest_json,'UTF8')),'hex')<>s.manifest_hash THEN
        RAISE EXCEPTION 'Invoice submission requires its immutable scoped maker/draft identity.' USING ERRCODE='23514';
      END IF;
      m:=s.manifest_json::jsonb; body:=d.snapshot_json::jsonb;
      IF jsonb_typeof(m) IS DISTINCT FROM 'object' OR jsonb_typeof(body) IS DISTINCT FROM 'object'
        OR m->>'Version' IS DISTINCT FROM 'client-sales-submission-v1'
        OR (m->>'DraftId')::uuid IS DISTINCT FROM d.id OR m->>'DraftHash' IS DISTINCT FROM d.snapshot_hash
        OR (m->>'ProfileId')::uuid IS DISTINCT FROM (body->>'ProfileId')::uuid
        OR (m->>'ProfileRevision')::bigint IS DISTINCT FROM (body->>'ProfileRevision')::bigint
        OR length(coalesce(m->>'NoTaxReason',''))>2000
        OR length(coalesce(m->>'SourceBasis',''))>2000
        OR (m->>'SourceReceiptId' IS NULL AND length(trim(coalesce(m->>'SourceBasis','')))=0)
        OR (m->>'SourceReceiptId' IS NULL AND m->>'SourceReceiptHash' IS NOT NULL)
        OR (m->>'SourceReceiptId' IS NOT NULL AND coalesce(m->>'SourceReceiptHash','') !~ '^[a-f0-9]{64}$')
        OR body->>'Version' IS DISTINCT FROM 'client-sales-draft-v1'
        OR body->>'EngineVersion' IS DISTINCT FROM 'native-invoice-line-net-v1'
        OR body->>'TaxTreatment' IS DISTINCT FROM 'NONE'
        OR body->>'Currency' IS DISTINCT FROM d.currency
        OR (body->'Seller'->>'ClientId')::uuid IS DISTINCT FROM d.client_id
        OR (body->'Customer'->>'ClientId')::uuid IS DISTINCT FROM d.client_id
        OR (body->'Customer'->>'Id')::uuid IS DISTINCT FROM d.customer_id
        OR (body->>'ChartVersionId')::uuid IS DISTINCT FROM d.chart_version_id
        OR body->>'DraftReference' IS DISTINCT FROM d.draft_reference
        OR body->>'SourceReference' IS DISTINCT FROM d.source_reference
        OR (body->>'Net')::numeric IS DISTINCT FROM d.net_amount
        OR (body->>'Gross')::numeric IS DISTINCT FROM d.gross_amount
        OR d.gross_amount<=0 OR d.net_amount<>d.gross_amount
        OR (body->>'DueDate')::date < (body->>'DocumentDate')::date
        OR jsonb_typeof(body->'Lines') IS DISTINCT FROM 'array'
        OR jsonb_array_length(body->'Lines') NOT BETWEEN 1 AND 100
        OR jsonb_typeof(m->'Lines') IS DISTINCT FROM 'array'
        OR jsonb_array_length(m->'Lines') NOT BETWEEN 2 AND 101 THEN
        RAISE EXCEPTION 'Invoice manifest, source declaration, dates and untaxed draft must agree.' USING ERRCODE='23514';
      END IF;
      decimals:=(body->'Policy'->>'DecimalPlaces')::integer; midpoint:=body->'Policy'->>'MidpointRule';
      IF decimals IS NULL OR decimals NOT BETWEEN 0 AND 6 OR midpoint IS NULL OR midpoint NOT IN ('TO_EVEN','AWAY_FROM_ZERO')
        OR body->'Policy'->>'Currency' IS DISTINCT FROM d.currency
        OR (body->'Policy'->>'ResidualTreatment') NOT IN ('REJECT','EXPLICIT_ACCOUNT')
        OR (body->'Policy'->>'MaximumResidualMinorUnits')::integer NOT BETWEEN 0 AND 3
        OR ((body->'Policy'->>'ResidualTreatment')='REJECT' AND
          ((body->'Policy'->>'MaximumResidualMinorUnits')::integer<>0 OR body->'Policy'->>'RoundingAccountCode'<>''))
        OR ((body->'Policy'->>'ResidualTreatment')='EXPLICIT_ACCOUNT' AND
          ((body->'Policy'->>'MaximumResidualMinorUnits')::integer=0 OR length(trim(coalesce(body->'Policy'->>'RoundingAccountCode','')))=0)) THEN
        RAISE EXCEPTION 'Invoice calculation requires its declared supported money policy.' USING ERRCODE='23514';
      END IF;
      n:=0; amount:=0;
      FOR item IN SELECT value FROM jsonb_array_elements(body->'Lines') LOOP
        n:=n+1;
        IF (item->>'LineNumber')::integer IS DISTINCT FROM n OR (item->>'AccountId')::uuid IS NULL
          OR length(trim(coalesce(item->>'AccountCode','')))=0 OR length(trim(coalesce(item->>'AccountName','')))=0
          OR length(trim(coalesce(item->>'Description',''))) NOT BETWEEN 1 AND 2000
          OR (item->>'Quantity')::numeric IS NULL OR (item->>'Quantity')::numeric NOT BETWEEN 0.000001 AND 1000000000
          OR round((item->>'Quantity')::numeric,6) IS DISTINCT FROM (item->>'Quantity')::numeric
          OR (item->>'UnitPrice')::numeric IS NULL OR (item->>'UnitPrice')::numeric NOT BETWEEN 0 AND 9999999999999.999999
          OR round((item->>'UnitPrice')::numeric,6) IS DISTINCT FROM (item->>'UnitPrice')::numeric
          OR (item->>'Discount')::numeric IS NULL OR (item->>'Discount')::numeric NOT BETWEEN 0 AND 9999999999999.999999
          OR round((item->>'Discount')::numeric,6) IS DISTINCT FROM (item->>'Discount')::numeric
          OR ((item->>'Quantity')::numeric*(item->>'UnitPrice')::numeric-(item->>'Discount')::numeric) NOT BETWEEN 0 AND 9999999999999.999999
          OR (item->>'Net')::numeric IS DISTINCT FROM client_sales_round(
            (item->>'Quantity')::numeric*(item->>'UnitPrice')::numeric-(item->>'Discount')::numeric,decimals,midpoint)
          OR (item->>'Gross')::numeric IS DISTINCT FROM (item->>'Net')::numeric THEN
          RAISE EXCEPTION 'Invoice source lines must reproduce their exact rounded untaxed values.' USING ERRCODE='23514';
        END IF;
        amount:=amount+(item->>'Net')::numeric;
      END LOOP;
      IF amount IS DISTINCT FROM d.gross_amount THEN RAISE EXCEPTION 'Invoice source line totals do not reconcile.' USING ERRCODE='23514'; END IF;
      SELECT * INTO rc FROM client_account_role_configurations WHERE firm_id=s.firm_id AND client_id=s.client_id
        AND id=(m->>'ReceivableRoleId')::uuid;
      SELECT * INTO rd FROM client_account_role_decisions WHERE firm_id=s.firm_id AND client_id=s.client_id
        AND id=(m->>'ReceivableDecisionId')::uuid AND configuration_id=rc.id;
      IF rc.id IS NULL OR rd.id IS NULL OR rc.role<>'AR' OR rd.decision<>'APPROVE'
        OR rd.reviewed_by_user_id=rc.proposed_by_user_id OR rc.account_id IS DISTINCT FROM (m->>'ReceivableAccountId')::uuid
        OR rc.chart_version_id<>d.chart_version_id OR rc.effective_from>(body->>'AccountingDate')::date
        OR rc.effective_to<(body->>'AccountingDate')::date THEN
        RAISE EXCEPTION 'Invoice receivable requires its independent scoped role and date/chart lineage.' USING ERRCODE='23514';
      END IF;
      SELECT snapshot_json INTO captured FROM client_operational_journal_snapshots
        WHERE firm_id=s.firm_id AND client_id=s.client_id AND journal_id=s.journal_id
          AND journal_revision=s.journal_submitted_revision AND capture_kind='SUBMISSION';
      header:=captured->'journal';
      IF captured IS NULL OR (header->>'id')::uuid IS DISTINCT FROM s.journal_id
        OR (header->>'firm_id')::uuid IS DISTINCT FROM s.firm_id OR (header->>'client_id')::uuid IS DISTINCT FROM s.client_id
        OR (header->>'period_id')::uuid IS DISTINCT FROM d.period_id
        OR (header->>'created_by_user_id')::uuid IS DISTINCT FROM s.created_by_user_id
        OR (header->>'revision')::bigint IS DISTINCT FROM s.journal_submitted_revision
        OR header->>'status' IS DISTINCT FROM 'SUBMITTED' OR header->>'submitted_at' IS NULL
        OR header->>'journal_number' IS DISTINCT FROM 'CLIENT-SALES-'||replace(s.invoice_id::text,'-','')
        OR header->>'description' IS DISTINCT FROM 'Client sales invoice '||d.draft_reference
        OR header->>'currency' IS DISTINCT FROM d.currency
        OR (header->>'posting_date')::date IS DISTINCT FROM (body->>'AccountingDate')::date
        OR jsonb_typeof(captured->'lines') IS DISTINCT FROM 'array' THEN
        RAISE EXCEPTION 'Invoice needs its exact database-captured native submitted journal.' USING ERRCODE='23514';
      END IF;
      SELECT jsonb_agg(jsonb_build_object('LineNumber',(x->>'LineNumber')::integer,'AccountId',(x->>'AccountId')::uuid,
        'Code',x->>'AccountCode','Name',x->>'AccountName','Description',x->>'Description',
        'Debit',(x->>'Debit')::numeric,'Credit',(x->>'Credit')::numeric) ORDER BY ordinal)
        INTO expected_lines FROM jsonb_array_elements(m->'Lines') WITH ORDINALITY e(x,ordinal);
      SELECT jsonb_agg(jsonb_build_object('LineNumber',(x->>'line_number')::integer,'AccountId',(x->>'client_account_id')::uuid,
        'Code',x->>'account_code','Name',x->>'account_name','Description',x->>'description',
        'Debit',(x->>'debit')::numeric,'Credit',(x->>'credit')::numeric) ORDER BY ordinal)
        INTO actual_lines FROM jsonb_array_elements(captured->'lines') WITH ORDINALITY e(x,ordinal);
      IF expected_lines IS DISTINCT FROM actual_lines OR EXISTS (
        SELECT 1 FROM jsonb_array_elements(captured->'lines') WITH ORDINALITY e(x,ordinal)
        WHERE (x->>'line_number')::integer IS DISTINCT FROM ordinal
          OR (x->>'firm_id')::uuid IS DISTINCT FROM s.firm_id OR (x->>'client_id')::uuid IS DISTINCT FROM s.client_id
          OR (x->>'journal_id')::uuid IS DISTINCT FROM s.journal_id) THEN
        RAISE EXCEPTION 'Invoice manifest lines must equal the retained submitted journal lines.' USING ERRCODE='23514';
      END IF;
      IF (m->'Lines'->0->>'LineNumber')::integer IS DISTINCT FROM 1
        OR (m->'Lines'->0->>'AccountId')::uuid IS DISTINCT FROM rc.account_id
        OR m->'Lines'->0->>'Description' IS DISTINCT FROM 'Invoice receivable'
        OR (m->'Lines'->0->>'Debit')::numeric IS DISTINCT FROM d.gross_amount
        OR (m->'Lines'->0->>'Credit')::numeric IS DISTINCT FROM 0::numeric THEN
        RAISE EXCEPTION 'Invoice control debit must equal its original gross amount.' USING ERRCODE='23514';
      END IF;
      SELECT jsonb_agg(jsonb_build_object('LineNumber',line_number,'AccountId',account_id,'Code',code,'Name',name,
        'Description','Invoice revenue','Debit',0::numeric,'Credit',net) ORDER BY line_number) INTO expected_lines
      FROM (SELECT 1+row_number() OVER (ORDER BY code COLLATE "C") AS line_number, account_id,code,name,net
        FROM (SELECT (x->>'AccountId')::uuid AS account_id,x->>'AccountCode' AS code,x->>'AccountName' AS name,
          sum((x->>'Net')::numeric) AS net FROM jsonb_array_elements(body->'Lines') e(x)
          GROUP BY (x->>'AccountId')::uuid,x->>'AccountCode',x->>'AccountName' HAVING sum((x->>'Net')::numeric)>0) totals) ordered;
      SELECT jsonb_agg(value ORDER BY ordinal) INTO actual_lines FROM jsonb_array_elements(actual_lines) WITH ORDINALITY e(value,ordinal) WHERE ordinal>1;
      IF expected_lines IS DISTINCT FROM actual_lines THEN
        RAISE EXCEPTION 'Invoice revenue credits must derive exclusively from the exact frozen source lines.' USING ERRCODE='23514';
      END IF;
      IF NOT validate_current THEN RETURN; END IF;
      SELECT * INTO p FROM client_accounting_profiles WHERE firm_id=s.firm_id AND client_id=s.client_id;
      SELECT * INTO mandate FROM acceptance_decisions WHERE firm_id=s.firm_id AND practice_client_id=s.client_id
        AND engagement_id IS NULL AND service_route='BOOKKEEPING' ORDER BY generation DESC,decided_at DESC,id DESC LIMIT 1;
      IF p.id IS DISTINCT FROM (m->>'ProfileId')::uuid OR p.revision IS DISTINCT FROM (m->>'ProfileRevision')::bigint
        OR p.source_mode IS DISTINCT FROM 'NATIVE_BOOKKEEPING' OR p.functional_currency IS DISTINCT FROM d.currency
        OR mandate.id IS NULL OR mandate.decision<>'Accepted' OR coalesce(mandate.conditions,'')<>''
        OR (require_original_mandate AND (mandate.id IS DISTINCT FROM (m->>'MandateId')::uuid
          OR mandate.generation IS DISTINCT FROM (m->>'MandateGeneration')::bigint))
        OR NOT EXISTS (SELECT 1 FROM client_reporting_periods period WHERE period.firm_id=s.firm_id AND period.client_id=s.client_id
          AND period.id=d.period_id AND period.status<>'CLOSED' AND period.currency=d.currency
          AND (body->>'AccountingDate')::date BETWEEN period.start_date AND period.end_date)
        OR (SELECT count(*) FROM client_chart_versions c WHERE c.firm_id=s.firm_id AND c.client_id=s.client_id
          AND c.status='APPROVED' AND c.effective_from<=(body->>'AccountingDate')::date
          AND (c.effective_to IS NULL OR c.effective_to>=(body->>'AccountingDate')::date))<>1
        OR NOT EXISTS (SELECT 1 FROM client_chart_versions c WHERE c.firm_id=s.firm_id AND c.client_id=s.client_id
          AND c.id=d.chart_version_id AND c.status='APPROVED' AND c.effective_from<=(body->>'AccountingDate')::date
          AND (c.effective_to IS NULL OR c.effective_to>=(body->>'AccountingDate')::date))
        OR EXISTS (SELECT 1 FROM jsonb_array_elements(m->'Lines') WITH ORDINALITY e(x,ordinal)
          LEFT JOIN client_accounts a ON a.firm_id=s.firm_id AND a.client_id=s.client_id AND a.id=(x->>'AccountId')::uuid
          WHERE a.id IS NULL OR a.chart_version_id<>d.chart_version_id OR NOT a.is_posting OR a.status<>'ACTIVE'
            OR a.account_code IS DISTINCT FROM x->>'AccountCode' OR a.account_name IS DISTINCT FROM x->>'AccountName'
            OR a.account_type IS DISTINCT FROM CASE WHEN ordinal=1 THEN 'ASSET' ELSE 'INCOME' END)
        OR (m->>'SourceReceiptId' IS NOT NULL AND NOT EXISTS (SELECT 1 FROM source_receipts r WHERE r.firm_id=s.firm_id
          AND r.client_id=s.client_id AND r.id=(m->>'SourceReceiptId')::uuid AND r.sha256_digest=m->>'SourceReceiptHash' AND r.byte_count>0)) THEN
        RAISE EXCEPTION 'Current invoice profile, accepted service, source, period and approved chart must still agree.' USING ERRCODE='23514';
      END IF;
    END; $fn$;

    CREATE FUNCTION guard_client_sales_workflow_history() RETURNS trigger LANGUAGE plpgsql AS $fn$
    BEGIN RAISE EXCEPTION 'Client invoice submissions, decisions and origin open items retain immutable history.' USING ERRCODE='23514'; END; $fn$;
    CREATE TRIGGER immutable_client_sales_submission BEFORE UPDATE OR DELETE ON client_sales_invoice_submissions
      FOR EACH ROW EXECUTE FUNCTION guard_client_sales_workflow_history();
    CREATE TRIGGER immutable_client_sales_decision BEFORE UPDATE OR DELETE ON client_sales_invoice_decisions
      FOR EACH ROW EXECUTE FUNCTION guard_client_sales_workflow_history();
    CREATE TRIGGER immutable_client_sales_open_item BEFORE UPDATE OR DELETE ON client_sales_invoice_open_items
      FOR EACH ROW EXECUTE FUNCTION guard_client_sales_workflow_history();

    CREATE FUNCTION guard_client_sales_submission_insert() RETURNS trigger LANGUAGE plpgsql AS $fn$
    DECLARE previous client_sales_invoice_submissions%ROWTYPE; journal client_operational_journals%ROWTYPE;
    BEGIN
      PERFORM id FROM practice_clients WHERE firm_id=NEW.firm_id AND id=NEW.client_id FOR UPDATE;
      SELECT * INTO journal FROM client_operational_journals WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id AND id=NEW.journal_id FOR UPDATE;
      IF journal.id IS NULL OR journal.created_by_user_id<>NEW.created_by_user_id OR journal.status<>'SUBMITTED'
        OR journal.revision<>NEW.journal_submitted_revision OR NOT EXISTS (
          SELECT 1 FROM client_sales_invoice_drafts d WHERE d.firm_id=NEW.firm_id AND d.client_id=NEW.client_id
            AND d.invoice_id=NEW.invoice_id AND d.id=NEW.draft_id AND d.revision=NEW.draft_revision
            AND d.created_by_user_id=NEW.created_by_user_id) OR EXISTS (
          SELECT 1 FROM client_sales_invoice_drafts d WHERE d.firm_id=NEW.firm_id AND d.client_id=NEW.client_id
            AND d.invoice_id=NEW.invoice_id AND d.revision>NEW.draft_revision) OR EXISTS (
          SELECT 1 FROM client_sales_invoice_submissions s WHERE s.firm_id=NEW.firm_id AND s.client_id=NEW.client_id
            AND s.journal_id=NEW.journal_id AND s.invoice_id<>NEW.invoice_id) OR EXISTS (
          SELECT 1 FROM client_sales_invoice_drafts own JOIN client_sales_invoice_drafts other
            ON other.firm_id=own.firm_id AND other.client_id=own.client_id AND other.invoice_id<>own.invoice_id
              AND other.source_reference=own.source_reference
          WHERE own.firm_id=NEW.firm_id AND own.client_id=NEW.client_id AND own.invoice_id=NEW.invoice_id
            AND own.id=NEW.draft_id AND length(own.source_reference)>0 AND EXISTS (
              SELECT 1 FROM client_sales_invoice_submissions accepted WHERE accepted.firm_id=NEW.firm_id
                AND accepted.client_id=NEW.client_id AND accepted.invoice_id=other.invoice_id
                AND NOT EXISTS (SELECT 1 FROM client_sales_invoice_decisions returned WHERE returned.firm_id=accepted.firm_id
                  AND returned.client_id=accepted.client_id AND returned.submission_id=accepted.id AND returned.decision='RETURN')) ) THEN
        RAISE EXCEPTION 'Invoice submission requires the latest maker draft and exact scoped native journal.' USING ERRCODE='23514';
      END IF;
      SELECT * INTO previous FROM client_sales_invoice_submissions WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id
        AND invoice_id=NEW.invoice_id ORDER BY draft_revision DESC LIMIT 1;
      IF previous.id IS NOT NULL AND (previous.journal_id<>NEW.journal_id OR previous.created_by_user_id<>NEW.created_by_user_id
        OR previous.draft_revision>=NEW.draft_revision OR previous.journal_submitted_revision>=NEW.journal_submitted_revision OR NOT EXISTS (
          SELECT 1 FROM client_sales_invoice_decisions d WHERE d.firm_id=previous.firm_id AND d.client_id=previous.client_id
            AND d.submission_id=previous.id AND d.decision='RETURN' AND d.actor_user_id<>NEW.created_by_user_id)) THEN
        RAISE EXCEPTION 'Invoice resubmission requires a newer source revision and independent return of the same journal.' USING ERRCODE='23514';
      END IF;
      RETURN NEW;
    END; $fn$;
    CREATE TRIGGER scoped_client_sales_submission BEFORE INSERT ON client_sales_invoice_submissions
      FOR EACH ROW EXECUTE FUNCTION guard_client_sales_submission_insert();

    CREATE FUNCTION guard_client_sales_draft_workflow() RETURNS trigger LANGUAGE plpgsql AS $fn$
    DECLARE latest client_sales_invoice_submissions%ROWTYPE;
    BEGIN
      PERFORM id FROM practice_clients WHERE firm_id=NEW.firm_id AND id=NEW.client_id FOR UPDATE;
      SELECT * INTO latest FROM client_sales_invoice_submissions WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id
        AND invoice_id=NEW.invoice_id ORDER BY draft_revision DESC LIMIT 1;
      IF latest.id IS NOT NULL AND (NEW.created_by_user_id<>latest.created_by_user_id OR NEW.revision<=latest.draft_revision OR NOT EXISTS (
        SELECT 1 FROM client_sales_invoice_decisions d WHERE d.firm_id=latest.firm_id AND d.client_id=latest.client_id
          AND d.submission_id=latest.id AND d.decision='RETURN' AND d.actor_user_id<>NEW.created_by_user_id)) THEN
        RAISE EXCEPTION 'Invoice drafts remain frozen until the exact submission is independently returned.' USING ERRCODE='23514';
      END IF;
      RETURN NEW;
    END; $fn$;
    CREATE TRIGGER client_sales_draft_workflow BEFORE INSERT ON client_sales_invoice_drafts
      FOR EACH ROW EXECUTE FUNCTION guard_client_sales_draft_workflow();

    CREATE FUNCTION guard_client_sales_decision_insert() RETURNS trigger LANGUAGE plpgsql AS $fn$
    DECLARE s client_sales_invoice_submissions%ROWTYPE; j client_operational_journals%ROWTYPE; context jsonb;
    BEGIN
      PERFORM id FROM practice_clients WHERE firm_id=NEW.firm_id AND id=NEW.client_id FOR UPDATE;
      SELECT * INTO s FROM client_sales_invoice_submissions WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id AND id=NEW.submission_id;
      SELECT * INTO j FROM client_operational_journals WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id AND id=s.journal_id FOR UPDATE;
      context:=NEW.review_context_json::jsonb;
      IF s.id IS NULL OR NEW.actor_user_id=s.created_by_user_id OR j.status IS DISTINCT FROM 'SUBMITTED'
        OR j.revision IS DISTINCT FROM s.journal_submitted_revision OR EXISTS (
          SELECT 1 FROM client_sales_invoice_submissions newer WHERE newer.firm_id=s.firm_id AND newer.client_id=s.client_id
            AND newer.invoice_id=s.invoice_id AND newer.draft_revision>s.draft_revision)
        OR encode(sha256(convert_to(NEW.review_context_json,'UTF8')),'hex')<>NEW.preview_digest
        OR context->>'Version' IS DISTINCT FROM 'client-sales-review-v1'
        OR (context->>'FirmId')::uuid IS DISTINCT FROM s.firm_id OR (context->>'ClientId')::uuid IS DISTINCT FROM s.client_id
        OR (context->>'ActorId')::uuid IS DISTINCT FROM NEW.actor_user_id OR (context->>'InvoiceId')::uuid IS DISTINCT FROM s.invoice_id
        OR (context->>'SubmissionId')::uuid IS DISTINCT FROM s.id OR context->>'ManifestHash' IS DISTINCT FROM s.manifest_hash
        OR (context->>'JournalId')::uuid IS DISTINCT FROM s.journal_id
        OR (context->>'JournalRevision')::bigint IS DISTINCT FROM s.journal_submitted_revision
        OR context->>'JournalStatus' IS DISTINCT FROM 'SUBMITTED'
        OR (context->>'AcceptedService')::boolean IS DISTINCT FROM true
        OR (NEW.decision='APPROVE' AND (coalesce(context->>'CurrentPreview','') !~ '^[a-f0-9]{64}$' OR context->>'PostingBlock' IS NOT NULL)) THEN
        RAISE EXCEPTION 'Independent invoice decision requires its exact current reviewed submission and context.' USING ERRCODE='23514';
      END IF;
      PERFORM assert_client_sales_submission(s.id,NEW.decision='APPROVE',false);
      IF NOT EXISTS (SELECT 1 FROM (SELECT decision,conditions FROM acceptance_decisions WHERE firm_id=s.firm_id
        AND practice_client_id=s.client_id AND engagement_id IS NULL AND service_route='BOOKKEEPING'
        ORDER BY generation DESC,decided_at DESC,id DESC LIMIT 1) current WHERE current.decision='Accepted' AND coalesce(current.conditions,'')='') THEN
        RAISE EXCEPTION 'Current accepted client bookkeeping service is required for review.' USING ERRCODE='23514';
      END IF;
      RETURN NEW;
    END; $fn$;
    CREATE TRIGGER independent_client_sales_decision BEFORE INSERT ON client_sales_invoice_decisions
      FOR EACH ROW EXECUTE FUNCTION guard_client_sales_decision_insert();

    CREATE FUNCTION check_client_sales_invoice_workflow() RETURNS trigger LANGUAGE plpgsql AS $fn$
    DECLARE invoice_key uuid; journal_key uuid; scope_firm uuid; scope_client uuid;
      s client_sales_invoice_submissions%ROWTYPE; decision client_sales_invoice_decisions%ROWTYPE;
      draft client_sales_invoice_drafts%ROWTYPE; journal client_operational_journals%ROWTYPE; captured jsonb;
    BEGIN
      IF TG_TABLE_NAME='client_sales_invoice_submissions' THEN
        invoice_key:=NEW.invoice_id; scope_firm:=NEW.firm_id; scope_client:=NEW.client_id;
        PERFORM assert_client_sales_submission(NEW.id,true,true);
      ELSIF TG_TABLE_NAME='client_sales_invoice_drafts' THEN
        invoice_key:=NEW.invoice_id; scope_firm:=NEW.firm_id; scope_client:=NEW.client_id;
      ELSIF TG_TABLE_NAME='client_sales_invoice_decisions' THEN
        SELECT * INTO s FROM client_sales_invoice_submissions WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id AND id=NEW.submission_id;
        invoice_key:=s.invoice_id; scope_firm:=s.firm_id; scope_client:=s.client_id;
      ELSIF TG_TABLE_NAME='client_sales_invoice_open_items' THEN
        SELECT * INTO s FROM client_sales_invoice_submissions WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id AND id=NEW.submission_id;
        invoice_key:=s.invoice_id; scope_firm:=s.firm_id; scope_client:=s.client_id;
        IF NEW.invoice_id<>s.invoice_id OR NEW.journal_id<>s.journal_id THEN
          RAISE EXCEPTION 'Invoice open item cannot change documentary or ledger identity.' USING ERRCODE='23514';
        END IF;
      ELSE
        IF TG_TABLE_NAME='client_operational_journals' THEN journal_key:=CASE WHEN TG_OP='DELETE' THEN OLD.id ELSE NEW.id END;
        ELSE journal_key:=CASE WHEN TG_OP='DELETE' THEN OLD.journal_id ELSE NEW.journal_id END; END IF;
        SELECT * INTO s FROM client_sales_invoice_submissions WHERE journal_id=journal_key ORDER BY draft_revision DESC LIMIT 1;
        IF s.id IS NULL THEN
          IF EXISTS (SELECT 1 FROM client_operational_journals j WHERE j.id=journal_key AND j.status IN ('SUBMITTED','POSTED')
            AND j.journal_number LIKE 'CLIENT-SALES-%') THEN
            RAISE EXCEPTION 'A client sales journal requires a proved invoice submission in the same transaction.' USING ERRCODE='23514';
          END IF;
          RETURN NULL;
        END IF;
        invoice_key:=s.invoice_id; scope_firm:=s.firm_id; scope_client:=s.client_id;
      END IF;
      SELECT * INTO s FROM client_sales_invoice_submissions WHERE firm_id=scope_firm AND client_id=scope_client
        AND invoice_id=invoice_key ORDER BY draft_revision DESC LIMIT 1;
      IF s.id IS NULL THEN RETURN NULL; END IF;
      PERFORM assert_client_sales_submission(s.id,false,false);
      SELECT * INTO draft FROM client_sales_invoice_drafts WHERE firm_id=s.firm_id AND client_id=s.client_id AND id=s.draft_id;
      SELECT * INTO journal FROM client_operational_journals WHERE firm_id=s.firm_id AND client_id=s.client_id AND id=s.journal_id;
      SELECT * INTO decision FROM client_sales_invoice_decisions WHERE firm_id=s.firm_id AND client_id=s.client_id AND submission_id=s.id;
      SELECT snapshot_json INTO captured FROM client_operational_journal_snapshots WHERE firm_id=s.firm_id AND client_id=s.client_id
        AND journal_id=s.journal_id AND journal_revision=s.journal_submitted_revision;
      IF journal.id IS NULL OR journal.status NOT IN ('SUBMITTED','RETURNED','POSTED')
        OR (to_jsonb(journal)-'status'-'revision'-'posted_by_user_id'-'posted_at') IS DISTINCT FROM
          ((captured->'journal')-'status'-'revision'-'posted_by_user_id'-'posted_at')
        OR (SELECT jsonb_agg(to_jsonb(l) ORDER BY l.line_number) FROM client_operational_journal_lines l
          WHERE l.firm_id=s.firm_id AND l.client_id=s.client_id AND l.journal_id=s.journal_id) IS DISTINCT FROM captured->'lines'
        OR (journal.status='SUBMITTED' AND (journal.revision<>s.journal_submitted_revision OR decision.id IS NOT NULL))
        OR (journal.status IN ('RETURNED','POSTED') AND (journal.revision<>s.journal_submitted_revision+1 OR decision.id IS NULL
          OR decision.decision IS DISTINCT FROM CASE WHEN journal.status='POSTED' THEN 'APPROVE' ELSE 'RETURN' END)) THEN
        RAISE EXCEPTION 'Invoice lifecycle requires the exact retained journal content and reviewed transition.' USING ERRCODE='23514';
      END IF;
      IF decision.id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM client_operational_journal_decisions d WHERE d.firm_id=s.firm_id
        AND d.client_id=s.client_id AND d.journal_id=s.journal_id AND d.journal_revision=s.journal_submitted_revision
        AND d.decision=decision.decision AND d.actor_user_id=decision.actor_user_id AND d.reason=decision.reason
        AND d.created_at=decision.created_at AND d.actor_user_id<>s.created_by_user_id) THEN
        RAISE EXCEPTION 'Invoice and native decisions must record the same independent exact review.' USING ERRCODE='23514';
      END IF;
      IF journal.status='SUBMITTED' AND EXISTS (SELECT 1 FROM client_operational_journal_decisions d WHERE d.firm_id=s.firm_id
        AND d.client_id=s.client_id AND d.journal_id=s.journal_id AND d.journal_revision=s.journal_submitted_revision) THEN
        RAISE EXCEPTION 'An invoice review cannot bypass its documentary decision and transition.' USING ERRCODE='23514';
      END IF;
      IF journal.status='POSTED' THEN
        IF journal.posted_by_user_id IS DISTINCT FROM decision.actor_user_id OR journal.posted_at IS DISTINCT FROM decision.created_at
          OR NOT EXISTS (SELECT 1 FROM client_operational_posting_receipts r WHERE r.firm_id=s.firm_id AND r.client_id=s.client_id
            AND r.journal_id=s.journal_id AND r.command_id=decision.command_id AND r.actor_user_id=decision.actor_user_id
            AND r.submitted_revision=s.journal_submitted_revision AND r.posted_revision=journal.revision
            AND r.intent_hash=decision.intent_hash AND r.preview_digest=decision.preview_digest AND r.recorded_at=journal.posted_at)
          OR NOT EXISTS (SELECT 1 FROM client_sales_invoice_open_items o WHERE o.firm_id=s.firm_id AND o.client_id=s.client_id
            AND o.invoice_id=s.invoice_id AND o.submission_id=s.id AND o.journal_id=s.journal_id AND o.customer_id=draft.customer_id
            AND o.currency=draft.currency AND o.original_amount=draft.gross_amount
            AND o.due_date=(draft.snapshot_json::jsonb->>'DueDate')::date AND o.posted_at=journal.posted_at) THEN
          RAISE EXCEPTION 'Invoice posting, exact receipt and original AR open item must commit atomically.' USING ERRCODE='23514';
        END IF;
      ELSIF EXISTS (SELECT 1 FROM client_sales_invoice_open_items o WHERE o.firm_id=s.firm_id AND o.client_id=s.client_id AND o.invoice_id=s.invoice_id)
        OR EXISTS (SELECT 1 FROM client_operational_posting_receipts r WHERE r.firm_id=s.firm_id AND r.client_id=s.client_id AND r.journal_id=s.journal_id) THEN
        RAISE EXCEPTION 'Unposted invoices cannot claim an open item or posting receipt.' USING ERRCODE='23514';
      END IF;
      RETURN NULL;
    END; $fn$;
    CREATE CONSTRAINT TRIGGER client_sales_submission_complete AFTER INSERT ON client_sales_invoice_submissions
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_client_sales_invoice_workflow();
    CREATE CONSTRAINT TRIGGER client_sales_decision_complete AFTER INSERT ON client_sales_invoice_decisions
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_client_sales_invoice_workflow();
    CREATE CONSTRAINT TRIGGER client_sales_open_item_complete AFTER INSERT ON client_sales_invoice_open_items
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_client_sales_invoice_workflow();
    CREATE CONSTRAINT TRIGGER client_sales_draft_complete AFTER INSERT ON client_sales_invoice_drafts
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_client_sales_invoice_workflow();
    CREATE CONSTRAINT TRIGGER client_sales_journal_complete AFTER INSERT OR UPDATE OR DELETE ON client_operational_journals
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_client_sales_invoice_workflow();
    CREATE CONSTRAINT TRIGGER client_sales_lines_complete AFTER INSERT OR UPDATE OR DELETE ON client_operational_journal_lines
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_client_sales_invoice_workflow();
    CREATE CONSTRAINT TRIGGER client_sales_native_decision_complete AFTER INSERT ON client_operational_journal_decisions
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_client_sales_invoice_workflow();
    CREATE CONSTRAINT TRIGGER client_sales_posting_receipt_complete AFTER INSERT ON client_operational_posting_receipts
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_client_sales_invoice_workflow();

    CREATE OR REPLACE FUNCTION guard_client_generic_control_post() RETURNS trigger LANGUAGE plpgsql AS $fn$
    DECLARE s client_sales_invoice_submissions%ROWTYPE;
    BEGIN
      IF NEW.status='POSTED' THEN
        PERFORM id FROM practice_clients WHERE firm_id=NEW.firm_id AND id=NEW.client_id FOR UPDATE;
        IF EXISTS (SELECT 1 FROM client_operational_journal_lines l JOIN client_account_role_configurations c
          ON c.account_id=l.client_account_id AND c.firm_id=l.firm_id AND c.client_id=l.client_id
          JOIN client_account_role_decisions d ON d.configuration_id=c.id AND d.firm_id=c.firm_id AND d.client_id=c.client_id
          WHERE l.firm_id=NEW.firm_id AND l.client_id=NEW.client_id AND l.journal_id=NEW.id AND d.decision='APPROVE'
            AND c.role IN ('AR','AP') AND c.effective_from<=NEW.posting_date AND (c.effective_to IS NULL OR c.effective_to>=NEW.posting_date)) THEN
          SELECT * INTO s FROM client_sales_invoice_submissions WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id
            AND journal_id=NEW.id AND journal_submitted_revision=NEW.revision-1;
          IF s.id IS NULL OR NOT EXISTS (SELECT 1 FROM client_sales_invoice_decisions d WHERE d.firm_id=s.firm_id
            AND d.client_id=s.client_id AND d.submission_id=s.id AND d.decision='APPROVE' AND d.actor_user_id<>s.created_by_user_id
            AND d.actor_user_id=NEW.posted_by_user_id AND d.created_at=NEW.posted_at) THEN
            RAISE EXCEPTION 'Generic journal cannot create unexplained AR/AP control activity.' USING ERRCODE='23514';
          END IF;
          PERFORM assert_client_sales_submission(s.id,true,false);
        END IF;
      END IF;
      RETURN NEW;
    END; $fn$;

      CREATE OR REPLACE FUNCTION check_client_operational_journal() RETURNS trigger LANGUAGE plpgsql AS $$
      DECLARE j client_operational_journals%ROWTYPE; journal_key uuid; submission_key uuid; n bigint; debit_total numeric; credit_total numeric;
      BEGIN
        IF TG_TABLE_NAME = 'client_operational_journals' THEN
          journal_key := CASE WHEN TG_OP = 'DELETE' THEN OLD.id ELSE NEW.id END;
        ELSE
          journal_key := CASE WHEN TG_OP = 'DELETE' THEN OLD.journal_id ELSE NEW.journal_id END;
        END IF;
        SELECT * INTO j FROM client_operational_journals WHERE id = journal_key;
        IF NOT FOUND OR j.status NOT IN ('SUBMITTED','POSTED') THEN RETURN NULL; END IF;
        SELECT count(*), coalesce(sum(debit),0), coalesce(sum(credit),0) INTO n,debit_total,credit_total
          FROM client_operational_journal_lines WHERE firm_id=j.firm_id AND client_id=j.client_id AND journal_id=j.id;
        IF n NOT BETWEEN 2 AND 101 OR debit_total <= 0 OR debit_total <> credit_total THEN
          RAISE EXCEPTION 'Native submitted or posted journal must balance' USING ERRCODE = '23514';
        END IF;
        IF n=101 THEN
          SELECT id INTO submission_key FROM client_sales_invoice_submissions WHERE firm_id=j.firm_id AND client_id=j.client_id
            AND journal_id=j.id AND journal_submitted_revision=CASE WHEN j.status='POSTED' THEN j.revision-1 ELSE j.revision END;
          IF submission_key IS NULL THEN
            RAISE EXCEPTION 'Manual native journals remain limited to 100 lines.' USING ERRCODE='23514';
          END IF;
          PERFORM assert_client_sales_submission(submission_key,false,false);
        END IF;
        IF NOT EXISTS (SELECT 1 FROM client_accounting_profiles p WHERE p.firm_id=j.firm_id AND p.client_id=j.client_id
          AND p.source_mode='NATIVE_BOOKKEEPING' AND p.functional_currency=j.currency) OR
          (SELECT count(*) FROM client_chart_versions c WHERE c.firm_id=j.firm_id AND c.client_id=j.client_id
            AND c.status='APPROVED' AND c.effective_from<=j.posting_date AND (c.effective_to IS NULL OR c.effective_to>=j.posting_date))<>1 OR EXISTS (
          SELECT 1 FROM client_operational_journal_lines l LEFT JOIN client_accounts a
            ON a.firm_id=l.firm_id AND a.client_id=l.client_id AND a.id=l.client_account_id
          LEFT JOIN client_chart_versions c ON c.id=a.chart_version_id AND c.firm_id=a.firm_id AND c.client_id=a.client_id
          WHERE l.journal_id=j.id AND (a.id IS NULL OR NOT a.is_posting OR a.status<>'ACTIVE'
            OR a.account_code<>l.account_code OR a.account_name<>l.account_name OR c.status<>'APPROVED'
            OR c.effective_from>j.posting_date OR c.effective_to<j.posting_date)) THEN
          RAISE EXCEPTION 'Native journal needs its matching profile and unique approved posting chart' USING ERRCODE = '23514';
        END IF;
        IF j.status = 'POSTED' AND NOT EXISTS (
          SELECT 1 FROM client_operational_journal_decisions d WHERE d.firm_id=j.firm_id AND d.client_id=j.client_id
            AND d.journal_id=j.id AND d.journal_revision=j.revision-1 AND d.decision='APPROVE'
            AND d.actor_user_id=j.posted_by_user_id AND d.actor_user_id<>j.created_by_user_id) THEN
          RAISE EXCEPTION 'Native posting needs an independent decision for its exact submitted revision' USING ERRCODE = '23514';
        END IF;
        RETURN NULL;
      END $$;
    """;

  internal const string Down = """

    DROP TRIGGER client_sales_posting_receipt_complete ON client_operational_posting_receipts;
    DROP TRIGGER client_sales_native_decision_complete ON client_operational_journal_decisions;
    DROP TRIGGER client_sales_lines_complete ON client_operational_journal_lines;
    DROP TRIGGER client_sales_journal_complete ON client_operational_journals;
    DROP TRIGGER client_sales_draft_complete ON client_sales_invoice_drafts;
    DROP TRIGGER client_sales_open_item_complete ON client_sales_invoice_open_items;
    DROP TRIGGER client_sales_decision_complete ON client_sales_invoice_decisions;
    DROP TRIGGER client_sales_submission_complete ON client_sales_invoice_submissions;
    DROP TRIGGER independent_client_sales_decision ON client_sales_invoice_decisions;
    DROP TRIGGER client_sales_draft_workflow ON client_sales_invoice_drafts;
    DROP TRIGGER scoped_client_sales_submission ON client_sales_invoice_submissions;
    DROP TRIGGER immutable_client_sales_open_item ON client_sales_invoice_open_items;
    DROP TRIGGER immutable_client_sales_decision ON client_sales_invoice_decisions;
    DROP TRIGGER immutable_client_sales_submission ON client_sales_invoice_submissions;
    DROP FUNCTION check_client_sales_invoice_workflow();
    DROP FUNCTION guard_client_sales_decision_insert();
    DROP FUNCTION guard_client_sales_draft_workflow();
    DROP FUNCTION guard_client_sales_submission_insert();
    DROP FUNCTION guard_client_sales_workflow_history();

                CREATE OR REPLACE FUNCTION guard_client_generic_control_post() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                  IF NEW.status='POSTED' THEN
                    PERFORM id FROM practice_clients WHERE firm_id=NEW.firm_id AND id=NEW.client_id FOR UPDATE;
                    IF EXISTS (SELECT 1 FROM client_operational_journal_lines l JOIN client_account_role_configurations c ON c.account_id=l.client_account_id AND c.firm_id=l.firm_id AND c.client_id=l.client_id
                       JOIN client_account_role_decisions d ON d.configuration_id=c.id AND d.firm_id=c.firm_id AND d.client_id=c.client_id
                       WHERE l.firm_id=NEW.firm_id AND l.client_id=NEW.client_id AND l.journal_id=NEW.id AND d.decision='APPROVE' AND c.role IN ('AR','AP') AND c.effective_from<=NEW.posting_date AND (c.effective_to IS NULL OR c.effective_to>=NEW.posting_date)) THEN
                      RAISE EXCEPTION 'Generic journal cannot create unexplained AR/AP control activity.' USING ERRCODE='23514';
                    END IF;
                  END IF;
                  RETURN NEW;
                END; $fn$;

      CREATE OR REPLACE FUNCTION check_client_operational_journal() RETURNS trigger LANGUAGE plpgsql AS $$
      DECLARE j client_operational_journals%ROWTYPE; journal_key uuid; n bigint; debit_total numeric; credit_total numeric;
      BEGIN
        IF TG_TABLE_NAME = 'client_operational_journals' THEN
          journal_key := CASE WHEN TG_OP = 'DELETE' THEN OLD.id ELSE NEW.id END;
        ELSE
          journal_key := CASE WHEN TG_OP = 'DELETE' THEN OLD.journal_id ELSE NEW.journal_id END;
        END IF;
        SELECT * INTO j FROM client_operational_journals WHERE id = journal_key;
        IF NOT FOUND OR j.status NOT IN ('SUBMITTED','POSTED') THEN RETURN NULL; END IF;
        SELECT count(*), coalesce(sum(debit),0), coalesce(sum(credit),0) INTO n,debit_total,credit_total
          FROM client_operational_journal_lines WHERE firm_id=j.firm_id AND client_id=j.client_id AND journal_id=j.id;
        IF n NOT BETWEEN 2 AND 100 OR debit_total <= 0 OR debit_total <> credit_total THEN
          RAISE EXCEPTION 'Native submitted or posted journal must balance' USING ERRCODE = '23514';
        END IF;
        IF NOT EXISTS (SELECT 1 FROM client_accounting_profiles p WHERE p.firm_id=j.firm_id AND p.client_id=j.client_id
          AND p.source_mode='NATIVE_BOOKKEEPING' AND p.functional_currency=j.currency) OR
          (SELECT count(*) FROM client_chart_versions c WHERE c.firm_id=j.firm_id AND c.client_id=j.client_id
            AND c.status='APPROVED' AND c.effective_from<=j.posting_date AND (c.effective_to IS NULL OR c.effective_to>=j.posting_date))<>1 OR EXISTS (
          SELECT 1 FROM client_operational_journal_lines l LEFT JOIN client_accounts a
            ON a.firm_id=l.firm_id AND a.client_id=l.client_id AND a.id=l.client_account_id
          LEFT JOIN client_chart_versions c ON c.id=a.chart_version_id AND c.firm_id=a.firm_id AND c.client_id=a.client_id
          WHERE l.journal_id=j.id AND (a.id IS NULL OR NOT a.is_posting OR a.status<>'ACTIVE'
            OR a.account_code<>l.account_code OR a.account_name<>l.account_name OR c.status<>'APPROVED'
            OR c.effective_from>j.posting_date OR c.effective_to<j.posting_date)) THEN
          RAISE EXCEPTION 'Native journal needs its matching profile and unique approved posting chart' USING ERRCODE = '23514';
        END IF;
        IF j.status = 'POSTED' AND NOT EXISTS (
          SELECT 1 FROM client_operational_journal_decisions d WHERE d.firm_id=j.firm_id AND d.client_id=j.client_id
            AND d.journal_id=j.id AND d.journal_revision=j.revision-1 AND d.decision='APPROVE'
            AND d.actor_user_id=j.posted_by_user_id AND d.actor_user_id<>j.created_by_user_id) THEN
          RAISE EXCEPTION 'Native posting needs an independent decision for its exact submitted revision' USING ERRCODE = '23514';
        END IF;
        RETURN NULL;
      END $$;

    DROP FUNCTION assert_client_sales_submission(uuid,boolean,boolean);
    DROP FUNCTION client_sales_round(numeric,integer,text);
    """;
}
