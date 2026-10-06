using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientOperationalJournalReversals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_operational_journal_reversals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_journal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reversal_journal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_revision = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    intent_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    prepared_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prepared_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_operational_journal_reversals", x => x.id);
                    table.CheckConstraint("ck_client_operational_journal_reversal", "original_journal_id<>reversal_journal_id AND original_revision>=1 AND length(trim(reason))>0 AND length(trim(evidence_reference))>0 AND intent_hash ~ '^[a-f0-9]{64}$'");
                    table.ForeignKey(
                        name: "FK_client_operational_journal_reversals_client_operational_jou~",
                        columns: x => new { x.firm_id, x.client_id, x.original_journal_id },
                        principalTable: "client_operational_journals",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_operational_journal_reversals_client_operational_jo~1",
                        columns: x => new { x.firm_id, x.client_id, x.reversal_journal_id },
                        principalTable: "client_operational_journals",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_operational_journal_reversals_users_firm_id_prepared~",
                        columns: x => new { x.firm_id, x.prepared_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_operational_journal_reversals_firm_id_client_id_orig~",
                table: "client_operational_journal_reversals",
                columns: new[] { "firm_id", "client_id", "original_journal_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_operational_journal_reversals_firm_id_client_id_reve~",
                table: "client_operational_journal_reversals",
                columns: new[] { "firm_id", "client_id", "reversal_journal_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_operational_journal_reversals_firm_id_prepared_by_us~",
                table: "client_operational_journal_reversals",
                columns: new[] { "firm_id", "prepared_by_user_id" });
            migrationBuilder.Sql("""
              CREATE FUNCTION protect_native_reversal_link() RETURNS trigger LANGUAGE plpgsql AS $$
              BEGIN
                IF TG_OP <> 'INSERT' THEN RAISE EXCEPTION 'Reversal lineage is immutable' USING ERRCODE='23514'; END IF;
                IF NOT EXISTS (SELECT 1 FROM client_operational_journals o JOIN client_operational_journals r
                  ON r.firm_id=o.firm_id AND r.client_id=o.client_id
                  WHERE o.id=NEW.original_journal_id AND r.id=NEW.reversal_journal_id AND o.firm_id=NEW.firm_id AND o.client_id=NEW.client_id
                    AND o.status='POSTED' AND o.revision=NEW.original_revision AND r.status='DRAFT' AND r.revision=1
                    AND r.currency=o.currency AND r.created_by_user_id=NEW.prepared_by_user_id) THEN
                  RAISE EXCEPTION 'Reversal needs the same-client immutable posted original and its new draft' USING ERRCODE='23514';
                END IF;
                RETURN NEW;
              END $$;
              CREATE TRIGGER native_reversal_link_guard BEFORE INSERT OR UPDATE OR DELETE ON client_operational_journal_reversals
                FOR EACH ROW EXECUTE FUNCTION protect_native_reversal_link();
              CREATE FUNCTION check_native_reversal_mirror(journal_key uuid) RETURNS void LANGUAGE plpgsql AS $$
              DECLARE link client_operational_journal_reversals%ROWTYPE;
              BEGIN
                SELECT * INTO link FROM client_operational_journal_reversals WHERE reversal_journal_id=journal_key;
                IF NOT FOUND THEN RETURN; END IF;
                IF EXISTS (
                  (SELECT line_number,client_account_id,account_code,account_name,description,credit,debit FROM client_operational_journal_lines
                    WHERE firm_id=link.firm_id AND client_id=link.client_id AND journal_id=link.original_journal_id
                   EXCEPT
                   SELECT line_number,client_account_id,account_code,account_name,description,debit,credit FROM client_operational_journal_lines
                    WHERE firm_id=link.firm_id AND client_id=link.client_id AND journal_id=link.reversal_journal_id)
                  UNION ALL
                  (SELECT line_number,client_account_id,account_code,account_name,description,debit,credit FROM client_operational_journal_lines
                    WHERE firm_id=link.firm_id AND client_id=link.client_id AND journal_id=link.reversal_journal_id
                   EXCEPT
                   SELECT line_number,client_account_id,account_code,account_name,description,credit,debit FROM client_operational_journal_lines
                    WHERE firm_id=link.firm_id AND client_id=link.client_id AND journal_id=link.original_journal_id)) THEN
                  RAISE EXCEPTION 'Full reversal must retain the exact original lines with swapped accounting sides' USING ERRCODE='23514';
                END IF;
                IF NOT EXISTS (SELECT 1 FROM client_operational_journals o JOIN client_operational_journals r
                  ON r.firm_id=o.firm_id AND r.client_id=o.client_id WHERE o.id=link.original_journal_id AND r.id=link.reversal_journal_id
                    AND o.status='POSTED' AND o.revision=link.original_revision AND o.currency=r.currency) THEN
                  RAISE EXCEPTION 'Reversal original and currency context cannot change' USING ERRCODE='23514';
                END IF;
              END $$;
              CREATE FUNCTION check_native_reversal_trigger() RETURNS trigger LANGUAGE plpgsql AS $$
              BEGIN
                IF TG_TABLE_NAME='client_operational_journal_reversals' THEN PERFORM check_native_reversal_mirror(NEW.reversal_journal_id);
                ELSIF TG_TABLE_NAME='client_operational_journals' THEN PERFORM check_native_reversal_mirror(NEW.id);
                ELSE
                  IF TG_OP <> 'INSERT' THEN PERFORM check_native_reversal_mirror(OLD.journal_id); END IF;
                  IF TG_OP <> 'DELETE' THEN PERFORM check_native_reversal_mirror(NEW.journal_id); END IF;
                END IF;
                RETURN NULL;
              END $$;
              CREATE CONSTRAINT TRIGGER native_reversal_link_mirror AFTER INSERT ON client_operational_journal_reversals
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_native_reversal_trigger();
              CREATE CONSTRAINT TRIGGER native_reversal_lines_mirror AFTER INSERT OR UPDATE OR DELETE ON client_operational_journal_lines
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_native_reversal_trigger();
              CREATE CONSTRAINT TRIGGER native_reversal_header_mirror AFTER UPDATE ON client_operational_journals
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_native_reversal_trigger();
              """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
              DROP TRIGGER native_reversal_header_mirror ON client_operational_journals;
              DROP TRIGGER native_reversal_lines_mirror ON client_operational_journal_lines;
              DROP TRIGGER native_reversal_link_mirror ON client_operational_journal_reversals;
              DROP TRIGGER native_reversal_link_guard ON client_operational_journal_reversals;
              DROP FUNCTION check_native_reversal_trigger();
              DROP FUNCTION check_native_reversal_mirror(uuid);
              DROP FUNCTION protect_native_reversal_link();
              """);

            migrationBuilder.DropTable(
                name: "client_operational_journal_reversals");
        }
    }
}
