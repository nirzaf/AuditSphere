using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientOperationalPostingReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_operational_posting_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    journal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submitted_revision = table.Column<long>(type: "bigint", nullable: false),
                    posted_revision = table.Column<long>(type: "bigint", nullable: false),
                    intent_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    preview_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_operational_posting_receipts", x => x.id);
                    table.CheckConstraint("ck_client_operational_posting_receipt", "command_id <> '00000000-0000-0000-0000-000000000000'::uuid AND submitted_revision >= 1 AND posted_revision=submitted_revision+1 AND intent_hash ~ '^[a-f0-9]{64}$' AND preview_digest ~ '^[a-f0-9]{64}$'");
                    table.ForeignKey(
                        name: "FK_client_operational_posting_receipts_client_operational_jour~",
                        columns: x => new { x.firm_id, x.client_id, x.journal_id },
                        principalTable: "client_operational_journals",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_operational_posting_receipts_users_firm_id_actor_use~",
                        columns: x => new { x.firm_id, x.actor_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_operational_posting_receipts_firm_id_actor_user_id",
                table: "client_operational_posting_receipts",
                columns: new[] { "firm_id", "actor_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_operational_posting_receipts_firm_id_client_id_comma~",
                table: "client_operational_posting_receipts",
                columns: new[] { "firm_id", "client_id", "command_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_operational_posting_receipts_firm_id_client_id_journ~",
                table: "client_operational_posting_receipts",
                columns: new[] { "firm_id", "client_id", "journal_id" },
                unique: true);
            migrationBuilder.Sql("""
              CREATE FUNCTION protect_client_operational_posting_receipt() RETURNS trigger LANGUAGE plpgsql AS $$
              BEGIN
                IF TG_OP <> 'INSERT' THEN
                  RAISE EXCEPTION 'Native posting receipts are immutable' USING ERRCODE='23514';
                END IF;
                IF NOT EXISTS (SELECT 1 FROM client_operational_journals j
                  JOIN client_operational_journal_decisions d ON d.firm_id=j.firm_id AND d.client_id=j.client_id AND d.journal_id=j.id
                  WHERE j.firm_id=NEW.firm_id AND j.client_id=NEW.client_id AND j.id=NEW.journal_id AND j.status='POSTED'
                    AND j.revision=NEW.posted_revision AND j.posted_by_user_id=NEW.actor_user_id AND j.posted_at=NEW.recorded_at
                    AND d.journal_revision=NEW.submitted_revision AND d.decision='APPROVE' AND d.actor_user_id=NEW.actor_user_id
                    AND NEW.actor_user_id<>j.created_by_user_id) THEN
                  RAISE EXCEPTION 'A receipt needs the matching committed posting and independent decision' USING ERRCODE='23514';
                END IF;
                RETURN NEW;
              END $$;
              CREATE TRIGGER trg_client_operational_posting_receipt BEFORE INSERT OR UPDATE OR DELETE
                ON client_operational_posting_receipts FOR EACH ROW EXECUTE FUNCTION protect_client_operational_posting_receipt();
              """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER trg_client_operational_posting_receipt ON client_operational_posting_receipts; DROP FUNCTION protect_client_operational_posting_receipt();");
            migrationBuilder.DropTable(
                name: "client_operational_posting_receipts");
        }
    }
}
