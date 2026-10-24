namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

internal static class ClientOpeningSuccessorProofSql
{
  internal const string Up = """
    CREATE FUNCTION protect_native_opening_successor_proof() RETURNS trigger LANGUAGE plpgsql AS $fn$
    BEGIN
      RAISE EXCEPTION 'Native successor opening source evidence is append-only.' USING ERRCODE='55000';
    END; $fn$;
    CREATE TRIGGER native_opening_successor_immutable
      BEFORE UPDATE OR DELETE ON client_operational_opening_successor_proofs
      FOR EACH ROW EXECUTE FUNCTION protect_native_opening_successor_proof();
    CREATE TRIGGER native_opening_successor_no_truncate
      BEFORE TRUNCATE ON client_operational_opening_successor_proofs
      FOR EACH STATEMENT EXECUTE FUNCTION protect_native_opening_successor_proof();

    CREATE FUNCTION verify_native_opening_successor_proof() RETURNS trigger LANGUAGE plpgsql AS $fn$
    DECLARE
      opening client_operational_opening_balances%ROWTYPE;
      predecessor client_reporting_periods%ROWTYPE;
      successor client_reporting_periods%ROWTYPE;
      source trial_balance_datasets%ROWTYPE;
      native native_trial_balance_sources%ROWTYPE;
    BEGIN
      SELECT * INTO opening FROM client_operational_opening_balances
        WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id AND id=NEW.opening_id;
      IF NOT FOUND OR opening.created_by_user_id<>NEW.actor_id OR opening.approved_by_user_id IS NOT NULL
         OR opening.evidence_sha256<>NEW.source_identity_hash
         OR opening.manifest_sha256 IS DISTINCT FROM (NEW.proof_json::jsonb->>'OpeningManifestHash') THEN
        RAISE EXCEPTION 'Successor proof must bind its unapproved original opening and source.' USING ERRCODE='23514';
      END IF;
      SELECT * INTO predecessor FROM client_reporting_periods
        WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id AND id=NEW.prior_period_id FOR SHARE;
      IF NOT FOUND OR predecessor.status<>'CLOSED' OR predecessor.revision<>NEW.prior_period_revision
         OR predecessor.closed_by_user_id IS NULL OR predecessor.closed_at IS NULL
         OR length(trim(COALESCE(predecessor.close_reason,'')))=0 THEN
        RAISE EXCEPTION 'Successor proof requires its exact actually closed predecessor.' USING ERRCODE='23514';
      END IF;
      SELECT * INTO successor FROM client_reporting_periods
        WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id AND id=opening.period_id FOR SHARE;
      IF NOT FOUND OR successor.prior_period_id IS DISTINCT FROM predecessor.id
         OR successor.start_date<>predecessor.end_date+1
         OR successor.currency<>predecessor.currency OR successor.basis<>predecessor.basis
         OR opening.as_of_date<>successor.start_date OR opening.currency<>successor.currency
         OR opening.period_revision<>successor.revision OR successor.status='CLOSED' THEN
        RAISE EXCEPTION 'Successor proof requires exact supported period lineage.' USING ERRCODE='23514';
      END IF;
      SELECT * INTO source FROM trial_balance_datasets
        WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id AND engagement_id=NEW.engagement_id
          AND id=NEW.source_dataset_id;
      IF NOT FOUND OR source.source_kind<>'Native' OR source.period_id IS DISTINCT FROM predecessor.id
         OR source.book_id IS NOT NULL OR source.import_state<>'SEALED'
         OR source.sha256_hex<>NEW.source_identity_hash OR source.validation_status<>'Accepted'
         OR NOT source.balanced OR source.control_total<>0
         OR source.currency IS DISTINCT FROM predecessor.currency OR source.basis IS DISTINCT FROM predecessor.basis
         OR NOT EXISTS (SELECT 1 FROM native_trial_balance_sources retained_native
           WHERE retained_native.firm_id=NEW.firm_id AND retained_native.client_id=NEW.client_id
             AND retained_native.engagement_id=NEW.engagement_id AND retained_native.dataset_id=source.id
             AND retained_native.source_identity_hash=NEW.source_identity_hash)
         OR NOT EXISTS (SELECT 1 FROM source_acceptance_decisions accepted
           WHERE accepted.firm_id=NEW.firm_id AND accepted.client_id=NEW.client_id
             AND accepted.engagement_id=NEW.engagement_id AND accepted.trial_balance_dataset_id=source.id
             AND accepted.source_kind='TB' AND accepted.decision='ACCEPTED'
             AND accepted.source_identity_hash=NEW.source_identity_hash
             AND accepted.accepted_by_user_id<>source.imported_by_user_id) THEN
        RAISE EXCEPTION 'Successor proof requires exact independently accepted native source evidence.' USING ERRCODE='23514';
      END IF;
      SELECT * INTO native FROM native_trial_balance_sources
        WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id AND engagement_id=NEW.engagement_id
          AND dataset_id=NEW.source_dataset_id;
      IF NOT FOUND OR NOT COALESCE((NEW.proof_json::jsonb->'Facts' @> jsonb_build_object(
          'TargetPeriodId', successor.id, 'TargetRevision', opening.period_revision,
          'PriorPeriodId', predecessor.id, 'PriorRevision', predecessor.revision,
          'SourceManifestHash', native.manifest_hash, 'SourceRowsHash', native.generated_rows_hash)), false)
         OR (native.manifest_json::jsonb->>'ActivityOnly') IS DISTINCT FROM 'false'
         OR (native.manifest_json::jsonb->>'AsOfDate') IS DISTINCT FROM predecessor.end_date::text THEN
        RAISE EXCEPTION 'Successor proof must retain its exact complete native source artifact.' USING ERRCODE='23514';
      END IF;
      -- Bind the original v1 monetary output to independently retained closing rows,
      -- not merely a self-hashed Facts object. Numeric comparison preserves all supported precision.
      IF jsonb_typeof(opening.manifest_json::jsonb->'Lines') IS DISTINCT FROM 'array'
         OR jsonb_typeof(native.generated_rows_json::jsonb) IS DISTINCT FROM 'array'
         OR EXISTS (
           SELECT 1 FROM jsonb_array_elements(native.generated_rows_json::jsonb) r
           LEFT JOIN client_accounts a ON a.id=(r.value->>'AccountId')::uuid
             AND a.firm_id=NEW.firm_id AND a.client_id=NEW.client_id
           WHERE a.id IS NULL OR NOT a.is_posting OR a.status<>'ACTIVE'
             OR a.chart_version_id IS DISTINCT FROM (opening.manifest_json::jsonb->>'ChartVersionId')::uuid
             OR a.account_code IS DISTINCT FROM r.value->>'AccountCode'
             OR a.account_name IS DISTINCT FROM r.value->>'AccountName'
             OR a.account_type NOT IN ('ASSET','LIABILITY','EQUITY','INCOME','EXPENSE')
             OR (a.account_type IN ('INCOME','EXPENSE') AND
               ((r.value->>'ClosingDebit')::numeric<>0 OR (r.value->>'ClosingCredit')::numeric<>0)))
         OR (
           SELECT jsonb_agg(jsonb_build_array(r.value->>'AccountCode', r.value->>'AccountName',
             (r.value->>'Debit')::numeric, (r.value->>'Credit')::numeric) ORDER BY r.value->>'AccountCode')
           FROM jsonb_array_elements(opening.manifest_json::jsonb->'Lines') r
         ) IS DISTINCT FROM (
           SELECT jsonb_agg(jsonb_build_array(r.value->>'AccountCode', r.value->>'AccountName',
             (r.value->>'ClosingDebit')::numeric, (r.value->>'ClosingCredit')::numeric) ORDER BY r.value->>'AccountCode')
           FROM jsonb_array_elements(native.generated_rows_json::jsonb) r
           JOIN client_accounts a ON a.id=(r.value->>'AccountId')::uuid
             AND a.firm_id=NEW.firm_id AND a.client_id=NEW.client_id
           WHERE a.account_type IN ('ASSET','LIABILITY','EQUITY')
             AND ((r.value->>'ClosingDebit')::numeric<>0 OR (r.value->>'ClosingCredit')::numeric<>0)
         ) THEN
        RAISE EXCEPTION 'Successor opening must reproduce its exact native closing rows.' USING ERRCODE='23514';
      END IF;
      RETURN NEW;
    END; $fn$;
    CREATE CONSTRAINT TRIGGER native_opening_successor_binding
      AFTER INSERT ON client_operational_opening_successor_proofs
      DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION verify_native_opening_successor_proof();
    """;

  internal const string Down = """
    DO $fn$ BEGIN
      IF EXISTS (SELECT 1 FROM client_operational_opening_successor_proofs) THEN
        RAISE EXCEPTION 'Cannot remove retained native successor opening evidence.' USING ERRCODE='55000';
      END IF;
    END; $fn$;
    DROP TRIGGER native_opening_successor_binding ON client_operational_opening_successor_proofs;
    DROP FUNCTION verify_native_opening_successor_proof();
    DROP TRIGGER native_opening_successor_no_truncate ON client_operational_opening_successor_proofs;
    DROP TRIGGER native_opening_successor_immutable ON client_operational_opening_successor_proofs;
    DROP FUNCTION protect_native_opening_successor_proof();
    """;
}
