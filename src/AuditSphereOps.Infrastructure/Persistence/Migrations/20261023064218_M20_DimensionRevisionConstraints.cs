using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

public partial class M20_DimensionRevisionConstraints : Migration
{
  protected override void Up(MigrationBuilder migrationBuilder)
  {
    migrationBuilder.Sql("""
      CREATE TABLE client_accounting_dimension_schema_revisions (
        id uuid NOT NULL,
        firm_id uuid NOT NULL,
        client_id uuid NOT NULL,
        schema_id uuid NOT NULL,
        revision bigint NOT NULL,
        base_revision_id uuid NULL,
        status character varying(30) NOT NULL,
        canonical_digest_version integer NOT NULL,
        canonical_digest character varying(64) NOT NULL,
        impact_acknowledged boolean NOT NULL,
        created_by_user_id uuid NOT NULL,
        created_at timestamp with time zone NOT NULL,
        validated_at timestamp with time zone NULL,
        published_by_user_id uuid NULL,
        published_at timestamp with time zone NULL,
        CONSTRAINT "PK_client_accounting_dimension_schema_revisions" PRIMARY KEY (id),
        CONSTRAINT "ak_client_dim_schema_revision_scope_id" UNIQUE (firm_id, client_id, id),
        CONSTRAINT "ak_client_dim_schema_revision_lineage_id" UNIQUE (firm_id, client_id, schema_id, id),
        CONSTRAINT "fk_client_dim_schema_client" FOREIGN KEY (firm_id, client_id)
          REFERENCES practice_clients (firm_id, id) ON DELETE RESTRICT,
        CONSTRAINT "fk_client_dim_schema_base" FOREIGN KEY (firm_id, client_id, schema_id, base_revision_id)
          REFERENCES client_accounting_dimension_schema_revisions (firm_id, client_id, schema_id, id) ON DELETE RESTRICT,
        CONSTRAINT "ck_client_dimension_schema_revision_values" CHECK (
          revision > 0 AND status IN ('DRAFT','SUBMITTED','APPROVED')
          AND canonical_digest_version > 0 AND canonical_digest ~ '^[0-9a-f]{64}$'
          AND ((revision = 1 AND base_revision_id IS NULL) OR (revision > 1 AND base_revision_id IS NOT NULL))
          AND ((status = 'DRAFT' AND validated_at IS NULL AND published_by_user_id IS NULL AND published_at IS NULL)
            OR (status = 'SUBMITTED' AND validated_at IS NOT NULL AND published_by_user_id IS NULL AND published_at IS NULL)
            OR (status = 'APPROVED' AND validated_at IS NOT NULL AND published_by_user_id IS NOT NULL
              AND published_at IS NOT NULL AND published_by_user_id <> created_by_user_id AND impact_acknowledged))
        )
      );

      CREATE UNIQUE INDEX "ux_client_dim_schema_owner_revision"
        ON client_accounting_dimension_schema_revisions (firm_id, client_id, revision);
      CREATE INDEX "ix_client_dim_schema_base"
        ON client_accounting_dimension_schema_revisions (firm_id, client_id, schema_id, base_revision_id);

      CREATE TABLE client_accounting_dimension_definition_revisions (
        schema_revision_id uuid NOT NULL,
        id uuid NOT NULL,
        firm_id uuid NOT NULL,
        client_id uuid NOT NULL,
        dimension_type character varying(40) NOT NULL,
        code character varying(100) NOT NULL,
        code_key character varying(100) NOT NULL,
        name character varying(300) NOT NULL,
        is_required boolean NOT NULL,
        CONSTRAINT "PK_client_accounting_dimension_definition_revisions" PRIMARY KEY (schema_revision_id, id),
        CONSTRAINT "ak_client_dim_definition_revision_scope_id" UNIQUE (firm_id, client_id, schema_revision_id, id),
        CONSTRAINT "fk_client_dim_definition_revision" FOREIGN KEY (firm_id, client_id, schema_revision_id)
          REFERENCES client_accounting_dimension_schema_revisions (firm_id, client_id, id) ON DELETE RESTRICT,
        CONSTRAINT "ck_client_dim_definition_revision_values" CHECK (
          dimension_type IN ('BRANCH','COST_CENTRE','DEPARTMENT','PROJECT','INTERCOMPANY_COUNTERPARTY')
          AND length(trim(code)) > 0 AND length(trim(code_key)) > 0 AND length(trim(name)) > 0
        )
      );
      CREATE UNIQUE INDEX "ux_client_dim_definition_revision_type"
        ON client_accounting_dimension_definition_revisions (firm_id, client_id, schema_revision_id, dimension_type);
      CREATE UNIQUE INDEX "ux_client_dim_definition_revision_code"
        ON client_accounting_dimension_definition_revisions (firm_id, client_id, schema_revision_id, dimension_type, code_key);

      CREATE TABLE client_accounting_dimension_value_revisions (
        schema_revision_id uuid NOT NULL,
        id uuid NOT NULL,
        firm_id uuid NOT NULL,
        client_id uuid NOT NULL,
        definition_id uuid NOT NULL,
        code character varying(100) NOT NULL,
        code_key character varying(100) NOT NULL,
        name character varying(300) NOT NULL,
        is_active boolean NOT NULL,
        effective_from date NOT NULL,
        effective_to date NULL,
        CONSTRAINT "PK_client_accounting_dimension_value_revisions" PRIMARY KEY (schema_revision_id, id),
        CONSTRAINT "ak_client_dim_value_revision_scope_id" UNIQUE (firm_id, client_id, schema_revision_id, id),
        CONSTRAINT "fk_client_dim_value_definition" FOREIGN KEY (firm_id, client_id, schema_revision_id, definition_id)
          REFERENCES client_accounting_dimension_definition_revisions (firm_id, client_id, schema_revision_id, id) ON DELETE RESTRICT,
        CONSTRAINT "ck_client_dim_value_revision_values" CHECK (
          length(trim(code)) > 0 AND length(trim(code_key)) > 0 AND length(trim(name)) > 0
          AND (effective_to IS NULL OR effective_to >= effective_from)
        )
      );
      CREATE UNIQUE INDEX "ux_client_dim_value_revision_code"
        ON client_accounting_dimension_value_revisions (firm_id, client_id, schema_revision_id, definition_id, code_key);
      """);

    migrationBuilder.Sql("""
      CREATE OR REPLACE FUNCTION guard_client_dim_schema_revision_insert()
      RETURNS trigger LANGUAGE plpgsql AS $$
      DECLARE predecessor_schema_id uuid; predecessor_revision bigint; predecessor_status text;
      BEGIN
        IF NEW.revision = 1 THEN
          IF NEW.base_revision_id IS NOT NULL THEN
            RAISE EXCEPTION 'The first schema revision cannot have a predecessor.' USING ERRCODE = '23514';
          END IF;
        ELSE
          SELECT schema_id, revision, status
            INTO predecessor_schema_id, predecessor_revision, predecessor_status
            FROM client_accounting_dimension_schema_revisions
            WHERE id = NEW.base_revision_id AND firm_id = NEW.firm_id AND client_id = NEW.client_id
            FOR SHARE;
          IF NOT FOUND OR predecessor_schema_id IS DISTINCT FROM NEW.schema_id
             OR predecessor_revision IS DISTINCT FROM NEW.revision - 1
             OR predecessor_status IS DISTINCT FROM 'APPROVED' THEN
            RAISE EXCEPTION 'A schema successor must name its exact adjacent published predecessor in the same owner lineage.'
              USING ERRCODE = '23514';
          END IF;
        END IF;
        RETURN NEW;
      END;
      $$;

      CREATE TRIGGER trg_client_dim_schema_revision_insert
        BEFORE INSERT ON client_accounting_dimension_schema_revisions
        FOR EACH ROW EXECUTE FUNCTION guard_client_dim_schema_revision_insert();

      CREATE OR REPLACE FUNCTION guard_client_dim_schema_revision_update()
      RETURNS trigger LANGUAGE plpgsql AS $$
      BEGIN
        IF TG_OP = 'DELETE' THEN
          RAISE EXCEPTION 'Dimension schema revision history cannot be deleted.' USING ERRCODE = '23514';
        END IF;
        IF NEW.id IS DISTINCT FROM OLD.id OR NEW.firm_id IS DISTINCT FROM OLD.firm_id
           OR NEW.client_id IS DISTINCT FROM OLD.client_id OR NEW.schema_id IS DISTINCT FROM OLD.schema_id
           OR NEW.revision IS DISTINCT FROM OLD.revision OR NEW.base_revision_id IS DISTINCT FROM OLD.base_revision_id
           OR NEW.created_by_user_id IS DISTINCT FROM OLD.created_by_user_id
           OR NEW.created_at IS DISTINCT FROM OLD.created_at THEN
          RAISE EXCEPTION 'Dimension schema revision identity and authorship are immutable.' USING ERRCODE = '23514';
        END IF;

        IF OLD.status = 'DRAFT' AND NEW.status = 'DRAFT' THEN
          IF (to_jsonb(NEW) - 'canonical_digest_version' - 'canonical_digest' - 'impact_acknowledged')
             IS DISTINCT FROM
             (to_jsonb(OLD) - 'canonical_digest_version' - 'canonical_digest' - 'impact_acknowledged') THEN
            RAISE EXCEPTION 'Only draft content digest metadata may change while a schema remains in DRAFT.'
              USING ERRCODE = '23514';
          END IF;
          RETURN NEW;
        END IF;

        IF OLD.status = 'DRAFT' AND NEW.status = 'SUBMITTED' THEN
          IF NEW.validated_at IS NULL OR
             (to_jsonb(NEW) - 'status' - 'validated_at') IS DISTINCT FROM
             (to_jsonb(OLD) - 'status' - 'validated_at') THEN
            RAISE EXCEPTION 'Schema validation may only record the validated state and time.' USING ERRCODE = '23514';
          END IF;
          IF NOT EXISTS (SELECT 1 FROM client_accounting_dimension_definition_revisions d
                         WHERE d.firm_id = NEW.firm_id AND d.client_id = NEW.client_id
                           AND d.schema_revision_id = NEW.id)
             OR EXISTS (
               SELECT 1 FROM client_accounting_dimension_definition_revisions d
               WHERE d.firm_id = NEW.firm_id AND d.client_id = NEW.client_id
                 AND d.schema_revision_id = NEW.id AND d.is_required
                 AND NOT EXISTS (SELECT 1 FROM client_accounting_dimension_value_revisions v
                   WHERE v.firm_id = d.firm_id AND v.client_id = d.client_id
                     AND v.schema_revision_id = d.schema_revision_id AND v.definition_id = d.id)) THEN
            RAISE EXCEPTION 'A validated schema requires definitions and values for every required definition.'
              USING ERRCODE = '23514';
          END IF;
          RETURN NEW;
        END IF;

        IF OLD.status = 'SUBMITTED' AND NEW.status = 'APPROVED' THEN
          IF NEW.published_at IS NULL OR NEW.published_by_user_id IS NULL
             OR NEW.published_by_user_id = NEW.created_by_user_id OR NOT NEW.impact_acknowledged
             OR (to_jsonb(NEW) - 'status' - 'published_by_user_id' - 'published_at' - 'impact_acknowledged')
                IS DISTINCT FROM
                (to_jsonb(OLD) - 'status' - 'published_by_user_id' - 'published_at' - 'impact_acknowledged') THEN
            RAISE EXCEPTION 'Schema publication may only record an independent publisher, time and impact acknowledgement.'
              USING ERRCODE = '23514';
          END IF;
          RETURN NEW;
        END IF;

        RAISE EXCEPTION 'Published or validated dimension schema revisions cannot be edited or transition backward.'
          USING ERRCODE = '23514';
      END;
      $$;

      CREATE TRIGGER trg_client_dim_schema_revision_update
        BEFORE UPDATE OR DELETE ON client_accounting_dimension_schema_revisions
        FOR EACH ROW EXECUTE FUNCTION guard_client_dim_schema_revision_update();

      CREATE OR REPLACE FUNCTION guard_client_dim_definition_revision()
      RETURNS trigger LANGUAGE plpgsql AS $$
      DECLARE owner_schema_id uuid; owner_status text;
      BEGIN
        IF TG_OP = 'DELETE' THEN
          SELECT status INTO owner_status FROM client_accounting_dimension_schema_revisions
            WHERE id = OLD.schema_revision_id AND firm_id = OLD.firm_id AND client_id = OLD.client_id FOR SHARE;
          IF NOT FOUND OR owner_status <> 'DRAFT' THEN
            RAISE EXCEPTION 'Only an unvalidated draft schema can remove a definition snapshot.' USING ERRCODE = '23514';
          END IF;
          RETURN OLD;
        END IF;

        SELECT schema_id, status INTO owner_schema_id, owner_status
          FROM client_accounting_dimension_schema_revisions
          WHERE id = NEW.schema_revision_id AND firm_id = NEW.firm_id AND client_id = NEW.client_id FOR SHARE;
        IF NOT FOUND OR owner_status <> 'DRAFT' THEN
          RAISE EXCEPTION 'Only an unvalidated draft schema can write definition snapshots.' USING ERRCODE = '23514';
        END IF;
        IF TG_OP = 'UPDATE' THEN
          IF NEW.id IS DISTINCT FROM OLD.id OR NEW.firm_id IS DISTINCT FROM OLD.firm_id
             OR NEW.client_id IS DISTINCT FROM OLD.client_id OR NEW.schema_revision_id IS DISTINCT FROM OLD.schema_revision_id
             OR NEW.dimension_type IS DISTINCT FROM OLD.dimension_type THEN
            RAISE EXCEPTION 'Definition identity, owner, revision and dimension type are stable.' USING ERRCODE = '23514';
          END IF;
          RETURN NEW;
        END IF;
        IF EXISTS (
          SELECT 1 FROM client_accounting_dimension_definition_revisions prior
          JOIN client_accounting_dimension_schema_revisions owner
            ON owner.id = prior.schema_revision_id AND owner.firm_id = prior.firm_id AND owner.client_id = prior.client_id
          WHERE prior.firm_id = NEW.firm_id AND prior.client_id = NEW.client_id
            AND owner.schema_id = owner_schema_id AND prior.id = NEW.id
            AND prior.schema_revision_id <> NEW.schema_revision_id
            AND prior.dimension_type IS DISTINCT FROM NEW.dimension_type) THEN
          RAISE EXCEPTION 'A stable definition identity cannot change its dimension type within a schema lineage.'
            USING ERRCODE = '23514';
        END IF;
        RETURN NEW;
      END;
      $$;

      CREATE TRIGGER trg_client_dim_definition_revision
        BEFORE INSERT OR UPDATE OR DELETE ON client_accounting_dimension_definition_revisions
        FOR EACH ROW EXECUTE FUNCTION guard_client_dim_definition_revision();

      CREATE OR REPLACE FUNCTION guard_client_dim_value_revision()
      RETURNS trigger LANGUAGE plpgsql AS $$
      DECLARE owner_schema_id uuid; owner_status text;
      BEGIN
        IF TG_OP = 'DELETE' THEN
          SELECT status INTO owner_status FROM client_accounting_dimension_schema_revisions
            WHERE id = OLD.schema_revision_id AND firm_id = OLD.firm_id AND client_id = OLD.client_id FOR SHARE;
          IF NOT FOUND OR owner_status <> 'DRAFT' THEN
            RAISE EXCEPTION 'Only an unvalidated draft schema can remove a value snapshot.' USING ERRCODE = '23514';
          END IF;
          RETURN OLD;
        END IF;

        SELECT schema_id, status INTO owner_schema_id, owner_status
          FROM client_accounting_dimension_schema_revisions
          WHERE id = NEW.schema_revision_id AND firm_id = NEW.firm_id AND client_id = NEW.client_id FOR SHARE;
        IF NOT FOUND OR owner_status <> 'DRAFT' THEN
          RAISE EXCEPTION 'Only an unvalidated draft schema can write value snapshots.' USING ERRCODE = '23514';
        END IF;
        IF TG_OP = 'UPDATE' THEN
          IF NEW.id IS DISTINCT FROM OLD.id OR NEW.firm_id IS DISTINCT FROM OLD.firm_id
             OR NEW.client_id IS DISTINCT FROM OLD.client_id OR NEW.schema_revision_id IS DISTINCT FROM OLD.schema_revision_id
             OR NEW.definition_id IS DISTINCT FROM OLD.definition_id THEN
            RAISE EXCEPTION 'Value identity, owner, revision and definition are stable.' USING ERRCODE = '23514';
          END IF;
          RETURN NEW;
        END IF;
        IF EXISTS (
          SELECT 1 FROM client_accounting_dimension_value_revisions prior
          JOIN client_accounting_dimension_schema_revisions owner
            ON owner.id = prior.schema_revision_id AND owner.firm_id = prior.firm_id AND owner.client_id = prior.client_id
          WHERE prior.firm_id = NEW.firm_id AND prior.client_id = NEW.client_id
            AND owner.schema_id = owner_schema_id AND prior.id = NEW.id
            AND prior.schema_revision_id <> NEW.schema_revision_id
            AND prior.definition_id IS DISTINCT FROM NEW.definition_id) THEN
          RAISE EXCEPTION 'A stable value identity cannot move to another definition within a schema lineage.'
            USING ERRCODE = '23514';
        END IF;
        RETURN NEW;
      END;
      $$;

      CREATE TRIGGER trg_client_dim_value_revision
        BEFORE INSERT OR UPDATE OR DELETE ON client_accounting_dimension_value_revisions
        FOR EACH ROW EXECUTE FUNCTION guard_client_dim_value_revision();
      """);
  }

  protected override void Down(MigrationBuilder migrationBuilder)
  {
    migrationBuilder.Sql("""
      LOCK TABLE client_accounting_dimension_schema_revisions,
        client_accounting_dimension_definition_revisions,
        client_accounting_dimension_value_revisions IN ACCESS EXCLUSIVE MODE;
      DO $client_dimension_schema_downgrade$
      BEGIN
        IF EXISTS (SELECT 1 FROM client_accounting_dimension_schema_revisions) THEN
          RAISE EXCEPTION 'Cannot drop retained client accounting dimension schema revisions.'
            USING ERRCODE = '55000';
        END IF;
      END;
      $client_dimension_schema_downgrade$;

      DROP TRIGGER trg_client_dim_value_revision ON client_accounting_dimension_value_revisions;
      DROP TRIGGER trg_client_dim_definition_revision ON client_accounting_dimension_definition_revisions;
      DROP TRIGGER trg_client_dim_schema_revision_update ON client_accounting_dimension_schema_revisions;
      DROP TRIGGER trg_client_dim_schema_revision_insert ON client_accounting_dimension_schema_revisions;
      DROP FUNCTION guard_client_dim_value_revision();
      DROP FUNCTION guard_client_dim_definition_revision();
      DROP FUNCTION guard_client_dim_schema_revision_update();
      DROP FUNCTION guard_client_dim_schema_revision_insert();
      DROP TABLE client_accounting_dimension_value_revisions;
      DROP TABLE client_accounting_dimension_definition_revisions;
      DROP TABLE client_accounting_dimension_schema_revisions;
      """);
  }
}
