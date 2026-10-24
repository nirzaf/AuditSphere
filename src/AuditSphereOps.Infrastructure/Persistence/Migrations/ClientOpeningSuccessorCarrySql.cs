namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

// Additive v2 guard. The original retained-output/source binding trigger still runs.
internal static class ClientOpeningSuccessorCarrySql
{
  internal const string Up = """
    CREATE FUNCTION verify_native_opening_successor_carry() RETURNS trigger LANGUAGE plpgsql AS $fn$
    DECLARE
      facts jsonb := NEW.proof_json::jsonb->'Facts';
      population jsonb := facts->'CarriedPopulation';
      members jsonb := population->'Items';
      member jsonb;
      native native_trial_balance_sources%ROWTYPE;
      prior_opening client_operational_opening_balances%ROWTYPE;
      prior_proof client_operational_opening_successor_proofs%ROWTYPE;
      membership jsonb;
      inherited jsonb;
      root_items jsonb;
      original jsonb;
      control jsonb;
      expected_count integer;
      actual_amount numeric;
      membership_name text;
      state_item jsonb;
      config_witness jsonb;
      decision_witness jsonb;
      configuration client_account_role_configurations%ROWTYPE;
      role_decision client_account_role_decisions%ROWTYPE;
    BEGIN
      IF NEW.proof_json::jsonb->>'Version'='native-opening-successor.v1' THEN RETURN NEW; END IF;
      IF NEW.proof_json::jsonb->>'Version' IS DISTINCT FROM 'native-opening-successor.v2' THEN
        RAISE EXCEPTION 'A recognized explicit successor proof version is required.' USING ERRCODE='23514';
      END IF;
      SELECT * INTO native FROM native_trial_balance_sources WHERE firm_id=NEW.firm_id
        AND client_id=NEW.client_id AND engagement_id=NEW.engagement_id AND dataset_id=NEW.source_dataset_id;
      IF NOT FOUND THEN RAISE EXCEPTION 'Missing exact native carried source.' USING ERRCODE='23514'; END IF;
      membership := (native.manifest_json::jsonb->'Pins'->>'MembershipJson')::jsonb;
      SELECT * INTO prior_opening FROM client_operational_opening_balances
        WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id AND period_id=NEW.prior_period_id;
      IF NOT FOUND OR prior_opening.approved_by_user_id IS NULL OR prior_opening.approved_at IS NULL
        OR prior_opening.approved_by_user_id=prior_opening.created_by_user_id
        OR membership->'Opening'->>'Id' IS DISTINCT FROM prior_opening.id::text
        OR membership->'Opening'->>'ManifestSha256' IS DISTINCT FROM prior_opening.manifest_sha256 THEN
        RAISE EXCEPTION 'Original carried opening must be independently approved and pinned.' USING ERRCODE='23514';
      END IF;
      SELECT * INTO prior_proof FROM client_operational_opening_successor_proofs
        WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id AND opening_id=prior_opening.id;
      IF FOUND THEN
        IF prior_proof.engagement_id<>NEW.engagement_id OR
          membership->'OpeningSuccessorProof'->>'ProofHash' IS DISTINCT FROM prior_proof.proof_hash THEN
          RAISE EXCEPTION 'Inherited carried proof is not the exact source member.' USING ERRCODE='23514';
        END IF;
        inherited := COALESCE(NULLIF(prior_proof.proof_json::jsonb->'Facts'->'CarriedPopulation'->'Items','null'::jsonb),'[]'::jsonb);
        root_items := '[]'::jsonb;
      ELSE
        inherited := '[]'::jsonb;
        root_items := COALESCE(NULLIF(prior_opening.manifest_json::jsonb->'OpenItems','null'::jsonb),'[]'::jsonb);
      END IF;
      IF jsonb_typeof(population) IS DISTINCT FROM 'object' OR
        population->>'Version' IS DISTINCT FROM 'native-successor-carried-population.v1' OR
        jsonb_typeof(members) IS DISTINCT FROM 'array' OR jsonb_array_length(members) NOT BETWEEN 1 AND 5000 OR
        (population->>'Count')::integer IS DISTINCT FROM jsonb_array_length(members) OR
        population->>'PopulationHash' IS DISTINCT FROM facts->>'PopulationHash' OR
        NOT COALESCE((population->>'PopulationHash') ~ '^[0-9a-f]{64}$',false) OR
        NOT COALESCE((population->>'ControlStateHash') ~ '^[0-9a-f]{64}$',false) OR
        jsonb_typeof(population->'ControlStateJson') IS DISTINCT FROM 'string' OR
        population->>'ControlStateHash' IS DISTINCT FROM encode(sha256(convert_to(population->>'ControlStateJson','UTF8')),'hex') OR
        jsonb_typeof((population->>'ControlStateJson')::jsonb) IS DISTINCT FROM 'array' OR
        jsonb_array_length((population->>'ControlStateJson')::jsonb)<>2 OR
        population->>'PopulationHash' IS DISTINCT FROM encode(sha256(convert_to(
          '{"Version":"native-successor-carried-population.v1","ControlStateHash":"'||(population->>'ControlStateHash')||'","Items":'||
          (NEW.proof_json::json->'Facts'->'CarriedPopulation'->'Items')::text||'}','UTF8')),'hex') OR
        jsonb_typeof(facts->'Controls') IS DISTINCT FROM 'array' OR jsonb_array_length(facts->'Controls')<>2 OR
        (SELECT count(DISTINCT x->>'OriginalOpenItemId') FROM jsonb_array_elements(members) x)<>jsonb_array_length(members) OR
        jsonb_array_length(membership->'SalesCredits')<>0 OR jsonb_array_length(membership->'PurchaseCredits')<>0 OR
        jsonb_array_length(membership->'Settlements')<>0 THEN
        RAISE EXCEPTION 'The complete original carried population is malformed or unsupported.' USING ERRCODE='23514';
      END IF;
      FOREACH membership_name IN ARRAY ARRAY['Sales','Purchases','SalesCredits','PurchaseCredits','Settlements','Lines'] LOOP
        IF jsonb_typeof(membership->membership_name) IS DISTINCT FROM 'array' THEN
          RAISE EXCEPTION 'Complete native original membership arrays are required.' USING ERRCODE='23514';
        END IF;
      END LOOP;
      expected_count := jsonb_array_length(inherited)+jsonb_array_length(root_items)+
        (SELECT count(*) FROM client_sales_invoice_open_items o
          WHERE o.firm_id=NEW.firm_id AND o.client_id=NEW.client_id AND EXISTS
            (SELECT 1 FROM jsonb_array_elements(membership->'Sales') pin WHERE pin->>'Id'=o.submission_id::text AND pin->>'JournalId'=o.journal_id::text))+
        (SELECT count(*) FROM client_purchase_invoice_open_items o
          WHERE o.firm_id=NEW.firm_id AND o.client_id=NEW.client_id AND EXISTS
            (SELECT 1 FROM jsonb_array_elements(membership->'Purchases') pin WHERE pin->>'Id'=o.submission_id::text AND pin->>'JournalId'=o.journal_id::text));
      IF expected_count<>jsonb_array_length(members) THEN
        RAISE EXCEPTION 'No original opening or native invoice member may be omitted or invented.' USING ERRCODE='23514';
      END IF;
      FOR member IN SELECT value FROM jsonb_array_elements(members) LOOP
        IF member->>'Currency' IS DISTINCT FROM prior_opening.currency OR
          NOT COALESCE((member->>'OriginalAmount') ~ '^[0-9]+(\.[0-9]{1,6})?$',false) OR
          (member->>'OriginalAmount')::numeric<=0 OR (member->>'BaselineAppliedAmount')::numeric IS DISTINCT FROM 0 OR
          (member->>'BaselineOpenAmount')::numeric IS DISTINCT FROM (member->>'OriginalAmount')::numeric OR
          jsonb_typeof(member->'SourceFactsJson') IS DISTINCT FROM 'string' OR
          NOT COALESCE((member->>'SourceFactsHash') ~ '^[0-9a-f]{64}$',false) OR
          member->>'SourceFactsHash' IS DISTINCT FROM encode(sha256(convert_to(member->>'SourceFactsJson','UTF8')),'hex') OR
          (member->>'SourceFactsJson')::jsonb->>'Version' IS DISTINCT FROM
            (CASE WHEN member->>'OriginalKind' IN ('OPENING_AR_INVOICE','OPENING_AP_INVOICE')
              THEN 'native-successor-opening-item-source.v1' ELSE 'native-successor-invoice-item-source.v1' END) OR
          NOT ((member->>'SourceFactsJson')::jsonb @> jsonb_build_object(
            'FirmId',NEW.firm_id,'ClientId',NEW.client_id,'OriginalKind',member->'OriginalKind',
            'OriginalOpenItemId',member->'OriginalOpenItemId','OriginalDocumentId',member->'OriginalDocumentId',
            'OriginalOpeningId',member->'OriginalOpeningId','SubmissionId',member->'SubmissionId',
            'DraftId',member->'DraftId','JournalId',member->'JournalId','CounterpartyId',member->'CounterpartyId',
            'Role',member->'Role','CounterpartyName',member->'CounterpartyName','AccountId',member->'AccountId','AccountCode',member->'AccountCode',
            'Currency',member->'Currency','OriginalAmount',member->'OriginalAmount','Reference',member->'Reference',
            'ReferenceKind',member->'ReferenceKind','DocumentDate',member->'DocumentDate',
            'AccountingDate',member->'AccountingDate','SourceAsOfDate',member->'SourceAsOfDate','DueDate',member->'DueDate')) OR
          NOT EXISTS (SELECT 1 FROM client_bookkeeping_counterparties party
            WHERE party.firm_id=NEW.firm_id AND party.client_id=NEW.client_id AND party.id=(member->>'CounterpartyId')::uuid) OR
          NOT EXISTS (SELECT 1 FROM jsonb_array_elements(facts->'Controls') c
            JOIN client_accounts account ON account.firm_id=NEW.firm_id AND account.client_id=NEW.client_id
              AND account.id=(c->>'AccountId')::uuid
            WHERE c->>'Role'=member->>'Role' AND c->>'AccountId'=member->>'AccountId'
              AND account.account_code=member->>'AccountCode') THEN
          RAISE EXCEPTION 'Carried item amount, original facts, party or control is invalid.' USING ERRCODE='23514';
        END IF;
        IF NOT EXISTS (SELECT 1 FROM jsonb_array_elements((population->>'ControlStateJson')::jsonb) c
          WHERE c->>'Role'=member->>'Role' AND c->>'AccountId'=member->>'AccountId'
            AND (member->>'SourceFactsJson')::jsonb @> jsonb_build_object(
              'ControlConfigurationId',c->'ConfigurationId','ControlConfigurationHash',c->'ConfigurationHash',
              'ControlDecisionId',c->'DecisionId','ControlDecisionHash',c->'DecisionHash','AccountHash',c->'AccountHash')) THEN
          RAISE EXCEPTION 'Original source control evidence does not match the actual retained control witness.' USING ERRCODE='23514';
        END IF;
        SELECT x INTO original FROM jsonb_array_elements(inherited) x
          WHERE x->>'OriginalKind'=member->>'OriginalKind' AND x->>'OriginalOpenItemId'=member->>'OriginalOpenItemId';
        IF FOUND THEN
          IF original IS DISTINCT FROM member THEN
            RAISE EXCEPTION 'Inherited original evidence cannot be rewritten.' USING ERRCODE='23514';
          END IF;
        ELSIF member->>'OriginalKind' IN ('OPENING_AR_INVOICE','OPENING_AP_INVOICE') THEN
          SELECT x INTO original FROM jsonb_array_elements(root_items) x WHERE x->>'Id'=member->>'OriginalOpenItemId';
          IF NOT FOUND OR member->>'OriginalDocumentId' IS DISTINCT FROM prior_opening.id::text OR
            member->>'OriginalOpeningId' IS DISTINCT FROM prior_opening.id::text OR
            member->>'Role' IS DISTINCT FROM original->>'Role' OR
            member->>'OriginalKind' IS DISTINCT FROM (CASE WHEN original->>'Role'='AR' THEN 'OPENING_AR_INVOICE' ELSE 'OPENING_AP_INVOICE' END) OR
            member->>'CounterpartyId' IS DISTINCT FROM original->>'CounterpartyId' OR
            member->>'CounterpartyName' IS DISTINCT FROM original->>'CounterpartyName' OR
            member->>'Reference' IS DISTINCT FROM original->>'Reference' OR member->>'ReferenceKind' IS DISTINCT FROM 'OPENING_REFERENCE' OR
            member->>'AccountId' IS DISTINCT FROM original->>'AccountId' OR member->>'AccountCode' IS DISTINCT FROM original->>'AccountCode' OR
            (member->>'OriginalAmount')::numeric IS DISTINCT FROM (original->>'Amount')::numeric OR
            member->>'DueDate' IS DISTINCT FROM original->>'DueDate' OR
            member->>'SourceAsOfDate' IS DISTINCT FROM prior_opening.as_of_date::text OR
            member->>'DocumentDate' IS NOT NULL OR member->>'AccountingDate' IS NOT NULL OR
            member->>'JournalId' IS NOT NULL OR member->>'SubmissionId' IS NOT NULL OR member->>'DraftId' IS NOT NULL THEN
            RAISE EXCEPTION 'Original reviewed opening item does not match its retained identity.' USING ERRCODE='23514';
          END IF;
        ELSIF member->>'OriginalKind'='PURCHASE_INVOICE' THEN
          SELECT jsonb_build_object('ItemId',o.id,'DocumentId',o.invoice_id,'SubmissionId',o.submission_id,
            'DraftId',s.draft_id,'JournalId',o.journal_id,'PartyId',o.supplier_id,'Currency',o.currency,
            'Amount',o.original_amount,'DueDate',o.due_date,'DocumentDate',d.document_date,
            'AccountingDate',d.accounting_date,'Reference',s.supplier_invoice_reference,
            'CounterpartyName',d.snapshot_json::jsonb->'SupplierName') INTO original
          FROM client_purchase_invoice_open_items o
          JOIN client_purchase_invoice_submissions s ON s.firm_id=o.firm_id AND s.client_id=o.client_id AND s.id=o.submission_id
          JOIN client_purchase_invoice_drafts d ON d.firm_id=s.firm_id AND d.client_id=s.client_id AND d.id=s.draft_id
          JOIN client_operational_journals j ON j.firm_id=o.firm_id AND j.client_id=o.client_id AND j.id=o.journal_id
          WHERE o.firm_id=NEW.firm_id AND o.client_id=NEW.client_id AND o.id=(member->>'OriginalOpenItemId')::uuid
            AND j.status='POSTED' AND j.period_id=NEW.prior_period_id
            AND EXISTS (SELECT 1 FROM client_purchase_invoice_decisions decision WHERE decision.firm_id=o.firm_id
              AND decision.client_id=o.client_id AND decision.submission_id=o.submission_id AND decision.decision='APPROVE'
              AND decision.actor_user_id<>s.created_by_user_id)
            AND EXISTS (SELECT 1 FROM jsonb_array_elements(membership->'Purchases') x WHERE x->>'Id'=s.id::text AND x->>'JournalId'=j.id::text
              AND x->>'SourceHash'=(member->>'SourceFactsJson')::jsonb->>'SubmissionHash')
            AND (member->>'SourceFactsJson')::jsonb->>'DraftSnapshotHash'=d.snapshot_hash
            AND (member->>'SourceFactsJson')::jsonb->>'SubmissionManifestHash'=s.manifest_hash
            AND EXISTS (SELECT 1 FROM client_purchase_invoice_decisions chosen WHERE chosen.firm_id=s.firm_id
              AND chosen.client_id=s.client_id AND chosen.submission_id=s.id AND chosen.decision='APPROVE'
              AND chosen.id=((member->>'SourceFactsJson')::jsonb->>'InvoiceDecisionId')::uuid)
            AND (s.manifest_json::jsonb->>'PayableAccountId') IS NOT DISTINCT FROM member->>'AccountId';
          IF NOT FOUND OR NOT (member @> jsonb_build_object('OriginalDocumentId',original->'DocumentId',
            'SubmissionId',original->'SubmissionId','DraftId',original->'DraftId','JournalId',original->'JournalId',
            'CounterpartyId',original->'PartyId','Currency',original->'Currency','DueDate',original->'DueDate',
            'DocumentDate',original->'DocumentDate','AccountingDate',original->'AccountingDate','Reference',original->'Reference',
            'CounterpartyName',original->'CounterpartyName')) OR
            member->>'Role' IS DISTINCT FROM 'AP' OR
            (member->>'OriginalAmount')::numeric IS DISTINCT FROM (original->>'Amount')::numeric THEN
            RAISE EXCEPTION 'Original posted purchase item is not an exact native source member.' USING ERRCODE='23514';
          END IF;
        ELSIF member->>'OriginalKind'='SALES_INVOICE' THEN
          SELECT jsonb_build_object('DocumentId',o.invoice_id,'SubmissionId',o.submission_id,'DraftId',s.draft_id,
            'JournalId',o.journal_id,'PartyId',o.customer_id,'Currency',o.currency,'Amount',o.original_amount,
            'DueDate',o.due_date,'DocumentDate',d.snapshot_json::jsonb->'DocumentDate',
            'AccountingDate',j.posting_date,'Reference',d.draft_reference,
            'CounterpartyName',d.snapshot_json::jsonb->'Customer'->'DisplayName') INTO original
          FROM client_sales_invoice_open_items o
          JOIN client_sales_invoice_submissions s ON s.firm_id=o.firm_id AND s.client_id=o.client_id AND s.id=o.submission_id
          JOIN client_sales_invoice_drafts d ON d.firm_id=s.firm_id AND d.client_id=s.client_id AND d.id=s.draft_id
          JOIN client_operational_journals j ON j.firm_id=o.firm_id AND j.client_id=o.client_id AND j.id=o.journal_id
          WHERE o.firm_id=NEW.firm_id AND o.client_id=NEW.client_id AND o.id=(member->>'OriginalOpenItemId')::uuid
            AND j.status='POSTED' AND j.period_id=NEW.prior_period_id
            AND EXISTS (SELECT 1 FROM client_sales_invoice_decisions decision WHERE decision.firm_id=o.firm_id
              AND decision.client_id=o.client_id AND decision.submission_id=o.submission_id AND decision.decision='APPROVE'
              AND decision.actor_user_id<>s.created_by_user_id)
            AND EXISTS (SELECT 1 FROM jsonb_array_elements(membership->'Sales') x WHERE x->>'Id'=s.id::text AND x->>'JournalId'=j.id::text
              AND x->>'SourceHash'=(member->>'SourceFactsJson')::jsonb->>'SubmissionHash')
            AND (member->>'SourceFactsJson')::jsonb->>'DraftSnapshotHash'=d.snapshot_hash
            AND (member->>'SourceFactsJson')::jsonb->>'SubmissionManifestHash'=s.manifest_hash
            AND EXISTS (SELECT 1 FROM client_sales_invoice_decisions chosen WHERE chosen.firm_id=s.firm_id
              AND chosen.client_id=s.client_id AND chosen.submission_id=s.id AND chosen.decision='APPROVE'
              AND chosen.id=((member->>'SourceFactsJson')::jsonb->>'InvoiceDecisionId')::uuid)
            AND (s.manifest_json::jsonb->>'ReceivableAccountId') IS NOT DISTINCT FROM member->>'AccountId';
          IF NOT FOUND OR NOT (member @> jsonb_build_object('OriginalDocumentId',original->'DocumentId',
            'SubmissionId',original->'SubmissionId','DraftId',original->'DraftId','JournalId',original->'JournalId',
            'CounterpartyId',original->'PartyId','Currency',original->'Currency','DueDate',original->'DueDate',
            'DocumentDate',original->'DocumentDate','AccountingDate',original->'AccountingDate','Reference',original->'Reference',
            'CounterpartyName',original->'CounterpartyName')) OR
            member->>'Role' IS DISTINCT FROM 'AR' OR
            (member->>'OriginalAmount')::numeric IS DISTINCT FROM (original->>'Amount')::numeric THEN
            RAISE EXCEPTION 'Original posted sales item is not an exact native source member.' USING ERRCODE='23514';
          END IF;
        ELSE
          RAISE EXCEPTION 'Unsupported original carried item kind.' USING ERRCODE='23514';
        END IF;
        IF member->>'OriginalKind' IN ('SALES_INVOICE','PURCHASE_INVOICE') THEN
          IF member->>'OriginalOpeningId' IS NOT NULL OR member->>'SourceAsOfDate' IS NOT NULL OR
            member->>'ReferenceKind' IS DISTINCT FROM (CASE WHEN member->>'OriginalKind'='SALES_INVOICE' THEN 'DRAFT_REFERENCE' ELSE 'SUPPLIER_INVOICE_REFERENCE' END) THEN
            RAISE EXCEPTION 'Native carried reference and unknown opening metadata cannot be invented.' USING ERRCODE='23514';
          END IF;
          -- Inherited original facts are already exact-json bound to their prior proof.
          IF NOT EXISTS (SELECT 1 FROM jsonb_array_elements(inherited) i WHERE i->>'OriginalOpenItemId'=member->>'OriginalOpenItemId') AND (
            NOT EXISTS (SELECT 1 FROM json_array_elements((native.manifest_json::json->'Pins'->>'MembershipJson')::json->'Journals') j
              WHERE j->>'Id'=member->>'JournalId' AND
                (member->>'SourceFactsJson')::jsonb->>'JournalHash'=encode(sha256(convert_to(j::text,'UTF8')),'hex')) OR
            NOT EXISTS (SELECT 1 FROM json_array_elements((native.manifest_json::json->'Pins'->>'MembershipJson')::json->'Decisions') d
              WHERE d->>'Id'=(member->>'SourceFactsJson')::jsonb->>'PostingDecisionId' AND d->>'JournalId'=member->>'JournalId'
                AND (member->>'SourceFactsJson')::jsonb->>'PostingDecisionHash'=encode(sha256(convert_to(d::text,'UTF8')),'hex')) OR
            NOT EXISTS (SELECT 1 FROM json_array_elements((native.manifest_json::json->'Pins'->>'MembershipJson')::json->'Receipts') r
              WHERE r->>'Id'=(member->>'SourceFactsJson')::jsonb->>'PostingReceiptId' AND r->>'JournalId'=member->>'JournalId'
                AND (member->>'SourceFactsJson')::jsonb->>'PostingReceiptHash'=encode(sha256(convert_to(r::text,'UTF8')),'hex')) OR
            NOT EXISTS (SELECT 1 FROM jsonb_array_elements(membership->'Submissions') s
              WHERE s->>'Id'=(member->>'SourceFactsJson')::jsonb->>'JournalSnapshotId' AND s->>'JournalId'=member->>'JournalId'
                AND s->>'SnapshotHash'=(member->>'SourceFactsJson')::jsonb->>'JournalSnapshotHash')) THEN
            RAISE EXCEPTION 'Native carried posting artifacts must be actual exact retained source members.' USING ERRCODE='23514';
          END IF;
        END IF;
        IF EXISTS (SELECT 1 FROM client_open_item_allocation_lines line WHERE line.firm_id=NEW.firm_id
          AND line.client_id=NEW.client_id AND line.target_kind=member->>'OriginalKind'
          AND line.target_open_item_id=(member->>'OriginalOpenItemId')::uuid) THEN
          RAISE EXCEPTION 'New successor capture cannot silently reinterpret prior allocation history.' USING ERRCODE='23514';
        END IF;
      END LOOP;
      IF (SELECT count(DISTINCT c->>'Role') FROM jsonb_array_elements(facts->'Controls') c WHERE c->>'Role' IN ('AR','AP'))<>2 THEN
        RAISE EXCEPTION 'Both distinct approved AR/AP controls are required.' USING ERRCODE='23514';
      END IF;
      FOR control IN SELECT value FROM jsonb_array_elements(facts->'Controls') LOOP
        SELECT value INTO state_item FROM jsonb_array_elements((population->>'ControlStateJson')::jsonb)
          WHERE value->>'Role'=control->>'Role' AND value @> control;
        IF NOT FOUND OR state_item->>'AccountHash' IS DISTINCT FROM encode(sha256(convert_to(state_item->>'AccountJson','UTF8')),'hex') OR
          state_item->>'ConfigurationHash' IS DISTINCT FROM encode(sha256(convert_to(state_item->>'ConfigurationJson','UTF8')),'hex') OR
          state_item->>'DecisionHash' IS DISTINCT FROM encode(sha256(convert_to(state_item->>'DecisionJson','UTF8')),'hex') OR
          NOT EXISTS (SELECT 1 FROM jsonb_array_elements(membership->'Accounts') account
            WHERE account=(state_item->>'AccountJson')::jsonb AND account->>'Id'=control->>'AccountId') THEN
          RAISE EXCEPTION 'Control witness must retain its exact source account and actual approved evidence.' USING ERRCODE='23514';
        END IF;
        SELECT * INTO configuration FROM client_account_role_configurations
          WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id AND id=(control->>'ConfigurationId')::uuid;
        SELECT * INTO role_decision FROM client_account_role_decisions
          WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id AND id=(control->>'DecisionId')::uuid;
        config_witness := (state_item->>'ConfigurationJson')::jsonb;
        decision_witness := (state_item->>'DecisionJson')::jsonb;
        IF configuration.id IS NULL OR role_decision.id IS NULL OR
          (config_witness-'ProposedAt') IS DISTINCT FROM jsonb_build_object(
            'Id',configuration.id,'FirmId',configuration.firm_id,'ClientId',configuration.client_id,
            'ChartVersionId',configuration.chart_version_id,'AccountId',configuration.account_id,'Role',configuration.role,
            'EffectiveFrom',configuration.effective_from,'EffectiveTo',configuration.effective_to,'Reason',configuration.reason,
            'ProposedByUserId',configuration.proposed_by_user_id) OR
          (config_witness->>'ProposedAt')::timestamptz IS DISTINCT FROM configuration.proposed_at OR
          (decision_witness-'ReviewedAt') IS DISTINCT FROM jsonb_build_object(
            'Id',role_decision.id,'FirmId',role_decision.firm_id,'ClientId',role_decision.client_id,
            'ConfigurationId',role_decision.configuration_id,'Decision',role_decision.decision,'Reason',role_decision.reason,
            'ReviewedByUserId',role_decision.reviewed_by_user_id) OR
          (decision_witness->>'ReviewedAt')::timestamptz IS DISTINCT FROM role_decision.reviewed_at THEN
          RAISE EXCEPTION 'Retained control witness differs from actual immutable configuration or review.' USING ERRCODE='23514';
        END IF;
        IF NOT EXISTS (SELECT 1 FROM client_account_role_configurations role
          JOIN client_account_role_decisions decision ON decision.firm_id=role.firm_id AND decision.client_id=role.client_id
            AND decision.configuration_id=role.id
          JOIN client_accounts account ON account.firm_id=role.firm_id AND account.client_id=role.client_id AND account.id=role.account_id
          JOIN client_reporting_periods predecessor ON predecessor.firm_id=role.firm_id AND predecessor.client_id=role.client_id
            AND predecessor.id=NEW.prior_period_id
          WHERE role.firm_id=NEW.firm_id AND role.client_id=NEW.client_id AND role.id=(control->>'ConfigurationId')::uuid
            AND role.role=control->>'Role' AND role.account_id=(control->>'AccountId')::uuid
            AND decision.id=(control->>'DecisionId')::uuid AND decision.decision='APPROVE'
            AND decision.reviewed_by_user_id<>role.proposed_by_user_id
            AND role.effective_from<=predecessor.start_date
            AND (role.effective_to IS NULL OR role.effective_to>=predecessor.end_date+1)
            AND account.is_posting AND account.status='ACTIVE'
            AND account.account_type=CASE WHEN role.role='AR' THEN 'ASSET' ELSE 'LIABILITY' END) OR
          NOT EXISTS (SELECT 1 FROM jsonb_array_elements(native.generated_rows_json::jsonb) r
            WHERE r->>'AccountId'=control->>'AccountId'
              AND (r->>'ClosingDebit')::numeric=(control->>'ClosingDebit')::numeric
              AND (r->>'ClosingCredit')::numeric=(control->>'ClosingCredit')::numeric)
            AND (EXISTS (SELECT 1 FROM jsonb_array_elements(native.generated_rows_json::jsonb) r WHERE r->>'AccountId'=control->>'AccountId')
              OR (control->>'ClosingDebit')::numeric<>0 OR (control->>'ClosingCredit')::numeric<>0) THEN
          RAISE EXCEPTION 'Carried control must reproduce its actual approved role and native closing row.' USING ERRCODE='23514';
        END IF;
        SELECT COALESCE(sum((x->>'BaselineOpenAmount')::numeric),0) INTO actual_amount
          FROM jsonb_array_elements(members) x WHERE x->>'Role'=control->>'Role' AND x->>'AccountId'=control->>'AccountId';
        IF actual_amount IS DISTINCT FROM (CASE WHEN control->>'Role'='AR'
          THEN (control->>'ClosingDebit')::numeric-(control->>'ClosingCredit')::numeric
          ELSE (control->>'ClosingCredit')::numeric-(control->>'ClosingDebit')::numeric END) OR actual_amount<0 THEN
          RAISE EXCEPTION 'Every carried control must reconcile to its complete exact original population.' USING ERRCODE='23514';
        END IF;
      END LOOP;
      IF EXISTS (SELECT 1 FROM jsonb_array_elements(membership->'Lines') line
        WHERE EXISTS (SELECT 1 FROM jsonb_array_elements(facts->'Controls') c WHERE c->>'AccountId'=line->>'ClientAccountId')
          AND NOT EXISTS (SELECT 1 FROM jsonb_array_elements(members) carried_item WHERE carried_item->>'JournalId'=line->>'JournalId')) THEN
        RAISE EXCEPTION 'Every predecessor control journal must retain a supported original invoice origin.' USING ERRCODE='23514';
      END IF;
      RETURN NEW;
    END; $fn$;
    CREATE CONSTRAINT TRIGGER native_opening_successor_carry_binding
      AFTER INSERT ON client_operational_opening_successor_proofs
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION verify_native_opening_successor_carry();
    """;

  internal const string Down = """
    DO $fn$ BEGIN
      IF EXISTS (SELECT 1 FROM client_operational_opening_successor_proofs
        WHERE proof_json::jsonb->>'Version'='native-opening-successor.v2') THEN
        RAISE EXCEPTION 'Cannot remove retained original-item successor evidence.' USING ERRCODE='55000';
      END IF;
    END; $fn$;
    DROP TRIGGER native_opening_successor_carry_binding ON client_operational_opening_successor_proofs;
    DROP FUNCTION verify_native_opening_successor_carry();
    """;
}
