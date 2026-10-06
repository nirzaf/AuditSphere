using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientOperationalJournalSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_operational_journal_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    journal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    journal_revision = table.Column<long>(type: "bigint", nullable: false),
                    capture_kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_operational_journal_snapshots", x => x.id);
                    table.CheckConstraint("ck_client_operational_snapshot_revision", "journal_revision >= 1 AND capture_kind = 'SUBMISSION' AND jsonb_typeof(snapshot_json) = 'object'");
                    table.ForeignKey(
                        name: "FK_client_operational_journal_snapshots_client_operational_jou~",
                        columns: x => new { x.firm_id, x.client_id, x.journal_id },
                        principalTable: "client_operational_journals",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_operational_journal_snapshots_firm_id_client_id_jour~",
                table: "client_operational_journal_snapshots",
                columns: new[] { "firm_id", "client_id", "journal_id", "journal_revision" },
                unique: true);
            migrationBuilder.Sql("""
              CREATE FUNCTION protect_client_journal_snapshot() RETURNS trigger LANGUAGE plpgsql AS $$
              BEGIN
                IF TG_OP<>'INSERT' OR pg_trigger_depth()<2 THEN
                  RAISE EXCEPTION 'Submitted journal snapshots are database-captured append-only evidence' USING ERRCODE='23514';
                END IF;
                RETURN NEW;
              END $$;
              CREATE TRIGGER client_journal_snapshot_immutable BEFORE INSERT OR UPDATE OR DELETE
                ON client_operational_journal_snapshots FOR EACH ROW EXECUTE FUNCTION protect_client_journal_snapshot();
              CREATE FUNCTION capture_client_journal_submission() RETURNS trigger LANGUAGE plpgsql AS $$
              BEGIN
                IF NEW.status='SUBMITTED' AND OLD.status IS DISTINCT FROM NEW.status THEN
                  INSERT INTO client_operational_journal_snapshots
                    (id,firm_id,client_id,journal_id,journal_revision,capture_kind,snapshot_json,captured_at)
                  VALUES (gen_random_uuid(),NEW.firm_id,NEW.client_id,NEW.id,NEW.revision,'SUBMISSION',
                    jsonb_build_object('journal',to_jsonb(NEW),'lines',
                      (SELECT jsonb_agg(to_jsonb(l) ORDER BY l.line_number) FROM client_operational_journal_lines l
                        WHERE l.firm_id=NEW.firm_id AND l.client_id=NEW.client_id AND l.journal_id=NEW.id)),statement_timestamp());
                END IF;
                RETURN NULL;
              END $$;
              CREATE TRIGGER client_journal_submission_capture AFTER UPDATE ON client_operational_journals
                FOR EACH ROW EXECUTE FUNCTION capture_client_journal_submission();
              """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
              DROP TRIGGER client_journal_submission_capture ON client_operational_journals;
              DROP FUNCTION capture_client_journal_submission();
              DROP TRIGGER client_journal_snapshot_immutable ON client_operational_journal_snapshots;
              DROP FUNCTION protect_client_journal_snapshot();
              """);
            migrationBuilder.DropTable(
                name: "client_operational_journal_snapshots");
        }
    }
}
