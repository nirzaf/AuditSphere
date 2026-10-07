namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

internal static class ClientOperationalOpeningBalanceSql
{
  internal const string Up = """
    CREATE FUNCTION protect_client_operational_opening_balance() RETURNS trigger LANGUAGE plpgsql AS $fn$
    BEGIN
      IF TG_OP='DELETE' THEN
        RAISE EXCEPTION 'Native opening-balance evidence is immutable.' USING ERRCODE='23514';
      END IF;
      IF TG_OP='INSERT' THEN
        IF NEW.approved_by_user_id IS NOT NULL OR NEW.approved_at IS NOT NULL THEN
          RAISE EXCEPTION 'Opening-balance evidence must be independently approved after creation.' USING ERRCODE='23514';
        END IF;
        RETURN NEW;
      END IF;
      IF OLD.approved_by_user_id IS NOT NULL OR OLD.approved_at IS NOT NULL OR
        NEW.approved_by_user_id IS NULL OR NEW.approved_at IS NULL OR
        NEW.approved_by_user_id=OLD.created_by_user_id OR
        (to_jsonb(NEW)-'approved_by_user_id'-'approved_at') IS DISTINCT FROM
        (to_jsonb(OLD)-'approved_by_user_id'-'approved_at') THEN
        RAISE EXCEPTION 'Only one independent approval may be appended to immutable opening evidence.' USING ERRCODE='23514';
      END IF;
      RETURN NEW;
    END; $fn$;
    CREATE TRIGGER client_operational_opening_balance_immutable
      BEFORE INSERT OR UPDATE OR DELETE ON client_operational_opening_balances
      FOR EACH ROW EXECUTE FUNCTION protect_client_operational_opening_balance();
    """;

  internal const string Down = """
    DROP TRIGGER client_operational_opening_balance_immutable ON client_operational_opening_balances;
    DROP FUNCTION protect_client_operational_opening_balance();
    """;
}
