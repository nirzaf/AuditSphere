using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientCounterpartyAmendments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_counterparty_amendments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    counterparty_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    display_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    address = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    tax_identifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    contact_details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    payment_terms = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    proposed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_counterparty_amendments", x => x.id);
                    table.UniqueConstraint("AK_client_counterparty_amendments_firm_id_client_id_counterpar~", x => new { x.firm_id, x.client_id, x.counterparty_id, x.id, x.revision });
                    table.CheckConstraint("ck_client_counterparty_amendment", "revision>1 AND length(trim(display_name))>0 AND length(trim(reason))>0");
                    table.ForeignKey(
                        name: "FK_client_counterparty_amendments_client_bookkeeping_counterpa~",
                        columns: x => new { x.firm_id, x.client_id, x.counterparty_id },
                        principalTable: "client_bookkeeping_counterparties",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_counterparty_amendments_users_firm_id_proposed_by_us~",
                        columns: x => new { x.firm_id, x.proposed_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "client_counterparty_amendment_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    counterparty_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amendment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_counterparty_amendment_decisions", x => x.id);
                    table.CheckConstraint("ck_client_counterparty_amendment_decision", "revision>1 AND decision IN ('APPROVE','REJECT') AND length(trim(reason))>0");
                    table.ForeignKey(
                        name: "FK_client_counterparty_amendment_decisions_client_counterparty~",
                        columns: x => new { x.firm_id, x.client_id, x.counterparty_id, x.amendment_id, x.revision },
                        principalTable: "client_counterparty_amendments",
                        principalColumns: new[] { "firm_id", "client_id", "counterparty_id", "id", "revision" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_counterparty_amendment_decisions_users_firm_id_revie~",
                        columns: x => new { x.firm_id, x.reviewed_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_counterparty_amendment_decisions_firm_id_client_id_~1",
                table: "client_counterparty_amendment_decisions",
                columns: new[] { "firm_id", "client_id", "counterparty_id", "amendment_id", "revision" });

            migrationBuilder.CreateIndex(
                name: "IX_client_counterparty_amendment_decisions_firm_id_client_id_a~",
                table: "client_counterparty_amendment_decisions",
                columns: new[] { "firm_id", "client_id", "amendment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_counterparty_amendment_decisions_firm_id_client_id_c~",
                table: "client_counterparty_amendment_decisions",
                columns: new[] { "firm_id", "client_id", "counterparty_id", "revision" },
                unique: true,
                filter: "decision = 'APPROVE'");

            migrationBuilder.CreateIndex(
                name: "IX_client_counterparty_amendment_decisions_firm_id_reviewed_by~",
                table: "client_counterparty_amendment_decisions",
                columns: new[] { "firm_id", "reviewed_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_counterparty_amendments_firm_id_proposed_by_user_id",
                table: "client_counterparty_amendments",
                columns: new[] { "firm_id", "proposed_by_user_id" });
            migrationBuilder.Sql("""
                CREATE FUNCTION guard_client_counterparty_review() RETURNS trigger LANGUAGE plpgsql AS $fn$
                DECLARE proposer uuid; current_revision bigint;
                BEGIN
                  PERFORM 1 FROM client_bookkeeping_counterparties WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id AND id=NEW.counterparty_id FOR UPDATE;
                  SELECT proposed_by_user_id INTO proposer FROM client_counterparty_amendments WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id AND id=NEW.amendment_id;
                  IF proposer IS NULL OR proposer=NEW.reviewed_by_user_id THEN
                    RAISE EXCEPTION 'Counterparty amendment review must be independent.' USING ERRCODE='23514';
                  END IF;
                  IF NEW.decision='APPROVE' THEN
                    SELECT COALESCE(MAX(revision),1) INTO current_revision FROM client_counterparty_amendment_decisions
                      WHERE firm_id=NEW.firm_id AND client_id=NEW.client_id AND counterparty_id=NEW.counterparty_id AND decision='APPROVE';
                    IF NEW.revision<>current_revision+1 THEN
                      RAISE EXCEPTION 'Counterparty amendment approval requires the current revision.' USING ERRCODE='23514';
                    END IF;
                  END IF;
                  RETURN NEW;
                END;
                $fn$;
                CREATE TRIGGER immutable_client_counterparty_amendment BEFORE UPDATE OR DELETE ON client_counterparty_amendments
                  FOR EACH ROW EXECUTE FUNCTION guard_client_counterparty_history();
                CREATE TRIGGER immutable_client_counterparty_amendment_decision BEFORE UPDATE OR DELETE ON client_counterparty_amendment_decisions
                  FOR EACH ROW EXECUTE FUNCTION guard_client_counterparty_history();
                CREATE TRIGGER independent_client_counterparty_review BEFORE INSERT ON client_counterparty_amendment_decisions
                  FOR EACH ROW EXECUTE FUNCTION guard_client_counterparty_review();
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER independent_client_counterparty_review ON client_counterparty_amendment_decisions;
                DROP TRIGGER immutable_client_counterparty_amendment_decision ON client_counterparty_amendment_decisions;
                DROP TRIGGER immutable_client_counterparty_amendment ON client_counterparty_amendments;
                DROP FUNCTION guard_client_counterparty_review();
                """);
            migrationBuilder.DropTable(
                name: "client_counterparty_amendment_decisions");

            migrationBuilder.DropTable(
                name: "client_counterparty_amendments");
        }
    }
}
