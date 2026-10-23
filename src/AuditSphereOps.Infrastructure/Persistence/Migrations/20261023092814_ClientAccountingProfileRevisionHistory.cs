using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientAccountingProfileRevisionHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_client_accounting_profile_scope_id",
                table: "client_accounting_profiles",
                columns: new[] { "firm_id", "client_id", "id" });

            migrationBuilder.CreateTable(
                name: "client_accounting_profile_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    previous_revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    jurisdiction = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    functional_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    fiscal_year_start_month = table.Column<int>(type: "integer", nullable: false),
                    fiscal_year_start_day = table.Column<int>(type: "integer", nullable: false),
                    source_system = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_system_identifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source_mode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_accounting_profile_revisions", x => x.id);
                    table.UniqueConstraint("ak_client_accounting_profile_revision_scope_id", x => new { x.firm_id, x.client_id, x.profile_id, x.id });
                    table.CheckConstraint("ck_client_accounting_profile_revision_values", "revision > 0 AND length(trim(status)) > 0 AND length(trim(jurisdiction)) > 0 AND functional_currency ~ '^[A-Z]{3}$' AND fiscal_year_start_month BETWEEN 1 AND 12 AND fiscal_year_start_day BETWEEN 1 AND 31 AND length(trim(source_system)) > 0 AND source_mode IN ('EXTERNAL_SOURCE', 'NATIVE_BOOKKEEPING') AND (reason IS NULL OR length(trim(reason)) BETWEEN 1 AND 2000)");
                    table.ForeignKey(
                        name: "fk_client_accounting_profile_revision_previous",
                        columns: x => new { x.firm_id, x.client_id, x.profile_id, x.previous_revision_id },
                        principalTable: "client_accounting_profile_revisions",
                        principalColumns: new[] { "firm_id", "client_id", "profile_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_client_accounting_profile_revision_profile",
                        columns: x => new { x.firm_id, x.client_id, x.profile_id },
                        principalTable: "client_accounting_profiles",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_client_accounting_profile_revision_created_by",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_accounting_profile_revisions_firm_id_client_id_profi~",
                table: "client_accounting_profile_revisions",
                columns: new[] { "firm_id", "client_id", "profile_id", "previous_revision_id" });

            migrationBuilder.CreateIndex(
                name: "ux_client_accounting_profile_revision",
                table: "client_accounting_profile_revisions",
                columns: new[] { "firm_id", "client_id", "profile_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_accounting_profile_revisions_firm_id_created_by_user~",
                table: "client_accounting_profile_revisions",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.Sql("""
              CREATE OR REPLACE FUNCTION guard_client_accounting_profile_revision_insert()
              RETURNS trigger LANGUAGE plpgsql AS $$
              DECLARE current_head client_accounting_profiles%ROWTYPE; prior_revision bigint;
              BEGIN
                SELECT * INTO current_head FROM client_accounting_profiles
                  WHERE firm_id = NEW.firm_id AND client_id = NEW.client_id AND id = NEW.profile_id
                  FOR SHARE;
                IF NOT FOUND OR NEW.revision IS DISTINCT FROM current_head.revision
                   OR NEW.status IS DISTINCT FROM current_head.status
                   OR NEW.jurisdiction IS DISTINCT FROM current_head.jurisdiction
                   OR NEW.functional_currency IS DISTINCT FROM current_head.functional_currency
                   OR NEW.fiscal_year_start_month IS DISTINCT FROM current_head.fiscal_year_start_month
                   OR NEW.fiscal_year_start_day IS DISTINCT FROM current_head.fiscal_year_start_day
                   OR NEW.source_system IS DISTINCT FROM current_head.source_system
                   OR NEW.source_system_identifier IS DISTINCT FROM current_head.source_system_identifier
                   OR NEW.source_mode IS DISTINCT FROM current_head.source_mode THEN
                  RAISE EXCEPTION 'A profile snapshot must exactly match its persisted owner-scoped profile revision.'
                    USING ERRCODE = '23514';
                END IF;

                IF NEW.revision = 1 THEN
                  IF NEW.previous_revision_id IS NOT NULL THEN
                    RAISE EXCEPTION 'The first profile revision cannot have a predecessor.' USING ERRCODE = '23514';
                  END IF;
                ELSIF NEW.previous_revision_id IS NOT NULL THEN
                  SELECT revision INTO prior_revision FROM client_accounting_profile_revisions
                    WHERE id = NEW.previous_revision_id AND firm_id = NEW.firm_id
                      AND client_id = NEW.client_id AND profile_id = NEW.profile_id
                    FOR SHARE;
                  IF NOT FOUND OR prior_revision IS DISTINCT FROM NEW.revision - 1 THEN
                    RAISE EXCEPTION 'A profile successor must name its exact adjacent predecessor in the same profile lineage.'
                      USING ERRCODE = '23514';
                  END IF;
                ELSIF EXISTS (
                  SELECT 1 FROM client_accounting_profile_revisions prior
                  WHERE prior.firm_id = NEW.firm_id AND prior.client_id = NEW.client_id
                    AND prior.profile_id = NEW.profile_id AND prior.revision = NEW.revision - 1
                ) THEN
                  RAISE EXCEPTION 'An available adjacent profile revision cannot be omitted from the predecessor link.'
                    USING ERRCODE = '23514';
                END IF;
                RETURN NEW;
              END;
              $$;

              CREATE CONSTRAINT TRIGGER trg_client_accounting_profile_revision_insert
                AFTER INSERT ON client_accounting_profile_revisions
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION guard_client_accounting_profile_revision_insert();

              CREATE OR REPLACE FUNCTION guard_client_accounting_profile_revision_immutable()
              RETURNS trigger LANGUAGE plpgsql AS $$
              BEGIN
                RAISE EXCEPTION 'Client accounting profile revision snapshots are immutable.' USING ERRCODE = '23514';
              END;
              $$;

              CREATE TRIGGER trg_client_accounting_profile_revision_immutable
                BEFORE UPDATE OR DELETE ON client_accounting_profile_revisions
                FOR EACH ROW EXECUTE FUNCTION guard_client_accounting_profile_revision_immutable();
              """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
              LOCK TABLE client_accounting_profile_revisions IN ACCESS EXCLUSIVE MODE;
              DO $client_accounting_profile_revision_downgrade$
              BEGIN
                IF EXISTS (SELECT 1 FROM client_accounting_profile_revisions) THEN
                  RAISE EXCEPTION 'Cannot drop retained client accounting profile revision snapshots.'
                    USING ERRCODE = '55000';
                END IF;
              END;
              $client_accounting_profile_revision_downgrade$;

              DROP TRIGGER trg_client_accounting_profile_revision_immutable ON client_accounting_profile_revisions;
              DROP TRIGGER trg_client_accounting_profile_revision_insert ON client_accounting_profile_revisions;
              DROP FUNCTION guard_client_accounting_profile_revision_immutable();
              DROP FUNCTION guard_client_accounting_profile_revision_insert();
              """);

            migrationBuilder.DropTable(
                name: "client_accounting_profile_revisions");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_client_accounting_profile_scope_id",
                table: "client_accounting_profiles");
        }
    }
}
